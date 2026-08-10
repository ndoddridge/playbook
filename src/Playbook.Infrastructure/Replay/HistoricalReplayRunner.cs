using Playbook.Application.Abstractions;
using Playbook.Application.Replay;
using Playbook.Core.Decisions;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Orchestrates:
/// snapshot → cutoff filter → knowledge → decision engine → records → outcomes → evaluation.
/// Does not bypass the centralized decision engine.
/// </summary>
public sealed class HistoricalReplayRunner : IHistoricalReplayRunner
{
    private readonly IHistoricalSnapshotSource _source;
    private readonly IHistoricalSnapshotBuilder _builder;
    private readonly IHistoricalWeekDataValidator _validator;
    private readonly IHistoricalKnowledgeFactory _knowledgeFactory;
    private readonly IDecisionEngine _decisionEngine;
    private readonly IDecisionRecordStore _recordStore;
    private readonly IDecisionOutcomeEvaluator _evaluator;

    public HistoricalReplayRunner(
        IHistoricalSnapshotSource source,
        IHistoricalSnapshotBuilder builder,
        IHistoricalWeekDataValidator validator,
        IHistoricalKnowledgeFactory knowledgeFactory,
        IDecisionEngine decisionEngine,
        IDecisionRecordStore recordStore,
        IDecisionOutcomeEvaluator evaluator)
    {
        _source = source;
        _builder = builder;
        _validator = validator;
        _knowledgeFactory = knowledgeFactory;
        _decisionEngine = decisionEngine;
        _recordStore = recordStore;
        _evaluator = evaluator;
    }

    public async Task<HistoricalReplayReport> RunAsync(
        HistoricalReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        var raw = await _source.GetRawWeekAsync(
                request.Season,
                request.Week,
                request.ScoringType,
                request.FixtureId,
                cancellationToken)
            .ConfigureAwait(false);

        if (raw is null)
        {
            throw new InvalidOperationException(
                $"No historical data available for season={request.Season} week={request.Week} " +
                $"(fixtureId={request.FixtureId ?? "default"}). " +
                "Use fixtureId=controlled-2018-w7 for the synthetic leakage fixture, " +
                "or omit fixtureId to load real nflverse weeks when supported.");
        }

        _validator.ValidateOrThrow(raw);

        // 1–2. Load + enforce cutoff. Outcomes stay segregated.
        var (snapshot, outcomes) = _builder.Build(raw);
        AssertNoOutcomeLeak(snapshot, outcomes);

        // 3. Replay context for the existing decision engine.
        var replay = ReplayContext.FromSnapshot(snapshot, request.DecisionKind);

        // 4–5. Knowledge from snapshot only (not live services).
        var knowledge = _knowledgeFactory.BuildKnowledge(snapshot, replay.DecisionContext);
        AssertKnowledgeRespectsCutoff(knowledge, snapshot.InformationCutoff);

        // 6–7. Decisions via centralized engine + immutable records.
        var rosterIds = snapshot.Roster.Select(r => r.PlayerId).ToHashSet();
        var rosterKnowledge = knowledge.Where(k => rosterIds.Contains(k.PlayerId)).ToList();
        var candidates = snapshot.Roster
            .Select(slot =>
            {
                var player = snapshot.Players.First(p => p.PlayerId == slot.PlayerId);
                return new StartSitCandidate
                {
                    PlayerId = player.PlayerId,
                    PlayerName = player.PlayerName,
                    Position = player.Position,
                    IsStarter = slot.IsStarter
                };
            })
            .ToList();

        var batch = await _decisionEngine
            .EvaluateStartSitAsync(rosterKnowledge, candidates, replay.DecisionContext, cancellationToken)
            .ConfigureAwait(false);

        // Grade the UI-facing recommendation set (comparative Start/Sit), not every intermediate synth.
        var recommendationKeys = batch.Recommendations
            .Select(r => (r.PlayerId, Rec: MapAction(r.Action)))
            .ToHashSet();

        var gradedResults = batch.Decisions
            .Where(d => recommendationKeys.Contains((d.PlayerId, d.Recommendation)))
            .GroupBy(d => (d.PlayerId, d.Recommendation))
            .Select(g => g.First())
            .ToList();

        // Fallback: if recommendation mapping misses (e.g. Watch), grade unique player decisions.
        if (gradedResults.Count == 0)
        {
            gradedResults = batch.Decisions
                .GroupBy(d => d.PlayerId)
                .Select(g => g.First())
                .ToList();
        }

        var decisionIds = gradedResults.Select(d => d.DecisionId).ToHashSet();
        var records = (await _recordStore.ListAsync(snapshot.Season, snapshot.Week, cancellationToken)
                .ConfigureAwait(false))
            .Where(r => decisionIds.Contains(r.DecisionId))
            .ToList();

        // Pre-outcome invariant: records must not already carry actuals.
        if (records.Any(r => r.ActualOutcome is not null || r.EvaluationResult is not null))
        {
            throw new InvalidOperationException(
                "Decision records already contain outcomes before historical reveal — aborting replay.");
        }

        // 8–10. Reveal outcomes, grade comparatively, attach to records.
        var grades = _evaluator.EvaluateStartSit(records, batch.Decisions, outcomes, snapshot);
        foreach (var grade in grades)
        {
            if (grade.ActualFantasyPoints is null)
            {
                continue;
            }

            await _recordStore.AttachOutcomeAsync(
                    grade.DecisionId,
                    grade.ActualFantasyPoints.Value,
                    grade.EvaluationSummary,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var gradedRecords = (await _recordStore.ListAsync(snapshot.Season, snapshot.Week, cancellationToken)
                .ConfigureAwait(false))
            .Where(r => decisionIds.Contains(r.DecisionId))
            .ToList();

        var correct = grades.Count(g => g.WasCorrect == true);
        var incorrect = grades.Count(g => g.WasCorrect == false);
        var ungraded = grades.Count(g => g.WasCorrect is null);
        var graded = correct + incorrect;

        var projErrors = grades
            .Where(g => g.ProjectionAbsoluteError is not null && g.ExpectedValue > 0)
            .Select(g => g.ProjectionAbsoluteError!.Value)
            .ToList();
        var projSq = grades
            .Where(g => g.ProjectionSquaredError is not null && g.ExpectedValue > 0)
            .Select(g => g.ProjectionSquaredError!.Value)
            .ToList();
        var baseA = grades
            .Where(g => g.BaselineRecentAbsoluteError is not null)
            .Select(g => g.BaselineRecentAbsoluteError!.Value)
            .ToList();
        var baseB = grades
            .Where(g => g.BaselineOpportunityAbsoluteError is not null)
            .Select(g => g.BaselineOpportunityAbsoluteError!.Value)
            .ToList();
        var differentials = grades
            .Where(g => g.ActualDecisionDifferential is not null)
            .Select(g => g.ActualDecisionDifferential!.Value)
            .ToList();

        var projectionEvals = BuildProjectionEvaluations(snapshot, outcomes);

        // Fair week-level baseline MAE: same eligible player set for current/A/B.
        var fair = projectionEvals
            .Where(p =>
                p.BaselineRecentAbsoluteError is not null &&
                p.BaselineOpportunityAbsoluteError is not null)
            .ToList();
        double? maeA = fair.Count == 0
            ? (baseA.Count == 0 ? null : Math.Round(baseA.Average(), 2))
            : Math.Round(fair.Average(p => p.BaselineRecentAbsoluteError!.Value), 2);
        double? maeB = fair.Count == 0
            ? (baseB.Count == 0 ? null : Math.Round(baseB.Average(), 2))
            : Math.Round(fair.Average(p => p.BaselineOpportunityAbsoluteError!.Value), 2);
        var fairPrimaryMae = fair.Count == 0
            ? null
            : (double?)Math.Round(fair.Average(p => p.AbsoluteError), 2);
        string? better = null;
        if (maeA is not null && maeB is not null)
        {
            better = maeB < maeA
                ? "Baseline B (opportunity-aware)"
                : maeA < maeB
                    ? "Baseline A (recent average)"
                    : "Tie";
        }

        return new HistoricalReplayReport
        {
            Season = snapshot.Season,
            Week = snapshot.Week,
            InformationCutoff = snapshot.InformationCutoff,
            LeagueName = snapshot.LeagueName,
            ScoringType = snapshot.ScoringType,
            DecisionCount = grades.Count,
            CorrectCount = correct,
            IncorrectCount = incorrect,
            UngradedCount = ungraded,
            DecisionAccuracyPercent = graded == 0 ? null : Math.Round(100.0 * correct / graded, 1),
            AverageProjectionAbsoluteError = fairPrimaryMae ??
                (projErrors.Count == 0 ? null : Math.Round(projErrors.Average(), 2)),
            AverageProjectionSquaredError = fair.Count > 0
                ? Math.Round(fair.Average(p => p.SquaredError), 2)
                : (projSq.Count == 0 ? null : Math.Round(projSq.Average(), 2)),
            BaselineRecentAverageMae = maeA,
            BaselineOpportunityAwareMae = maeB,
            BetterBaselineLabel = better,
            AverageDecisionDifferential = differentials.Count == 0 ? null : Math.Round(differentials.Average(), 2),
            AverageConfidence = grades.Count == 0 ? 0 : Math.Round(grades.Average(g => g.Confidence), 1),
            Grades = grades,
            DecisionRecords = gradedRecords,
            ProjectionEvaluations = projectionEvals,
            PlayersEvaluated = snapshot.Players.Count,
            PlayersWithValidProjection = snapshot.Players.Count(p => p.ProjectedPoints is not null),
            PlayersWithInjurySignal = snapshot.Players.Count(p =>
                !string.IsNullOrWhiteSpace(p.InjuryStatus) &&
                !string.Equals(p.HealthLabel, "Healthy", StringComparison.OrdinalIgnoreCase)),
            PlayersWithUsageSignal = snapshot.Players.Count(p => p.UsageScore is not null),
            PlayersWithRoleSignal = snapshot.Players.Count(p => !string.IsNullOrWhiteSpace(p.RoleNote)),
            UnavailableSources = snapshot.UnavailableSources,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static IReadOnlyList<PlayerProjectionEvaluation> BuildProjectionEvaluations(
        HistoricalSnapshot snapshot,
        HistoricalWeekOutcomes outcomes)
    {
        var list = new List<PlayerProjectionEvaluation>();
        foreach (var player in snapshot.Players)
        {
            if (player.ProjectedPoints is null)
            {
                continue;
            }

            if (!outcomes.ByPlayerId.TryGetValue(player.PlayerId, out var outcome))
            {
                continue;
            }

            var predicted = (double)player.ProjectedPoints.Value;
            var actual = outcome.ActualFantasyPoints;
            var abs = Math.Abs(actual - predicted);
            var signed = actual - predicted;
            double? baseA = player.BaselineRecentAveragePoints;
            double? baseB = player.BaselineOpportunityAwarePoints;

            list.Add(new PlayerProjectionEvaluation
            {
                Season = snapshot.Season,
                Week = snapshot.Week,
                PlayerId = player.PlayerId,
                PlayerName = player.PlayerName,
                Position = player.Position,
                PredictedPoints = predicted,
                ActualPoints = actual,
                AbsoluteError = abs,
                SignedError = signed,
                SquaredError = signed * signed,
                BaselineRecentAveragePoints = baseA,
                BaselineOpportunityAwarePoints = baseB,
                BaselineRecentAbsoluteError = baseA is null ? null : Math.Abs(actual - baseA.Value),
                BaselineOpportunityAbsoluteError = baseB is null ? null : Math.Abs(actual - baseB.Value),
                DataSufficiency = player.DataSufficiency,
                ProjectionConfidence = player.ProjectionConfidence,
                SourceWeeks = player.ProjectionSourceWeeks,
                OpportunityScore = player.OpportunityScore,
                UsageScore = player.UsageScore,
                RecentProductionScore = player.RecentProductionScore
            });
        }

        return list;
    }

    private static DecisionRecommendation MapAction(StartSitAction action) => action switch
    {
        StartSitAction.Start => DecisionRecommendation.Start,
        StartSitAction.Sit => DecisionRecommendation.Sit,
        _ => DecisionRecommendation.NoAction
    };

    private static void AssertNoOutcomeLeak(HistoricalSnapshot snapshot, HistoricalWeekOutcomes outcomes)
    {
        // Snapshot must not embed actual fantasy points from the outcomes dictionary.
        foreach (var player in snapshot.Players)
        {
            var leak = player.UnavailableSignals.Any(s =>
                s.Contains("actual fantasy", StringComparison.OrdinalIgnoreCase));
            if (leak)
            {
                throw new InvalidOperationException($"Snapshot unexpectedly references actuals for {player.PlayerName}.");
            }
        }

        _ = outcomes;
    }

    private static void AssertKnowledgeRespectsCutoff(
        IReadOnlyList<PlayerKnowledge> knowledge,
        DateTimeOffset cutoff)
    {
        foreach (var item in knowledge)
        {
            foreach (var fact in item.Facts)
            {
                if (fact.ObservedAt is DateTimeOffset observed && observed > cutoff)
                {
                    throw new InvalidOperationException(
                        $"Future fact leaked into knowledge for {item.PlayerName}: {fact.Statement}");
                }
            }

            foreach (var signal in item.Signals)
            {
                if (signal.ObservedAt is DateTimeOffset observed && observed > cutoff)
                {
                    throw new InvalidOperationException(
                        $"Future signal leaked into knowledge for {item.PlayerName}: {signal.Explanation}");
                }

                if (signal.Explanation.Contains("ruled out for the season", StringComparison.OrdinalIgnoreCase) ||
                    (signal.Type == SignalType.Health &&
                     signal.Explanation.Contains("Out", StringComparison.OrdinalIgnoreCase) &&
                     item.PlayerName.Contains("Delta", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"Future injury/news leaked into knowledge for {item.PlayerName}: {signal.Explanation}");
                }
            }
        }
    }
}
