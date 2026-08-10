using Microsoft.Extensions.Logging;
using Playbook.Application.Predictions;
using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Predictions;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Measures LabRoster vs ExpandedSkillUniverse coverage. Count-only — no model tuning.
/// </summary>
public sealed class HistoricalEvaluationCoverageRunner
{
    private readonly IHistoricalSnapshotSource _source;
    private readonly IHistoricalSnapshotBuilder _builder;
    private readonly IMultiWeekHistoricalReplayRunner _seasonReplay;
    private readonly HistoricalQuickPickGenerator _quickPickGenerator;
    private readonly ILogger<HistoricalEvaluationCoverageRunner> _logger;

    public HistoricalEvaluationCoverageRunner(
        IHistoricalSnapshotSource source,
        IHistoricalSnapshotBuilder builder,
        IMultiWeekHistoricalReplayRunner seasonReplay,
        HistoricalQuickPickGenerator quickPickGenerator,
        ILogger<HistoricalEvaluationCoverageRunner> logger)
    {
        _source = source;
        _builder = builder;
        _seasonReplay = seasonReplay;
        _quickPickGenerator = quickPickGenerator;
        _logger = logger;
    }

    public async Task<HistoricalEvaluationCoverageReport> RunOfficialCoverageAsync(
        CancellationToken cancellationToken = default)
    {
        var development = await MeasureSeasonAsync(
                FrozenHistoricalEvaluationCoverageV1.DevelopmentSeason,
                FrozenHistoricalEvaluationCoverageV1.DevelopmentStartWeek,
                FrozenHistoricalEvaluationCoverageV1.DevelopmentEndWeek,
                role: "Development (frozen-benchmark season; counts only)",
                cancellationToken)
            .ConfigureAwait(false);

        // Holdout is measured for coverage counts only — never used to select thresholds.
        var holdout = await MeasureSeasonAsync(
                FrozenHistoricalEvaluationCoverageV1.HoldoutSeason,
                FrozenHistoricalEvaluationCoverageV1.HoldoutStartWeek,
                FrozenHistoricalEvaluationCoverageV1.HoldoutEndWeek,
                role: "Holdout (isolated; coverage counts only — not used for tuning)",
                cancellationToken)
            .ConfigureAwait(false);

        return new HistoricalEvaluationCoverageReport
        {
            ProtocolId = FrozenHistoricalEvaluationCoverageV1.ProtocolId,
            GeneratedAt = DateTimeOffset.UtcNow,
            Development = development,
            Holdout = holdout,
            HoldoutIsolated = true,
            Frozen2018BenchmarkUnchanged = true
        };
    }

    public async Task<HistoricalCoverageSeasonCompare> MeasureSeasonAsync(
        int season,
        int startWeek,
        int endWeek,
        string role,
        CancellationToken cancellationToken = default)
    {
        var beforeRaw = await AggregateAsync(
                season,
                startWeek,
                endWeek,
                HistoricalCandidateUniverse.LabRoster,
                cancellationToken)
            .ConfigureAwait(false);
        var after = await AggregateAsync(
                season,
                startWeek,
                endWeek,
                HistoricalCandidateUniverse.ExpandedSkillUniverse,
                cancellationToken)
            .ConfigureAwait(false);
        var before = WithLabCapExclusion(beforeRaw, after);

        _logger.LogInformation(
            "Coverage {Season}: StartSitCandidates {BeforeSs}->{AfterSs}; QPPreds {BeforeQp}->{AfterQp}",
            season,
            before.StartSitCandidates,
            after.StartSitCandidates,
            before.QuickPickPredictions,
            after.QuickPickPredictions);

        return new HistoricalCoverageSeasonCompare
        {
            Season = season,
            StartWeek = startWeek,
            EndWeek = endWeek,
            Role = role,
            Before = before,
            After = after
        };
    }

    private async Task<HistoricalCoverageSliceCounts> AggregateAsync(
        int season,
        int startWeek,
        int endWeek,
        HistoricalCandidateUniverse universe,
        CancellationToken cancellationToken)
    {
        var weeksLoaded = 0;
        var playerIds = new HashSet<Guid>();
        var playerWeeks = 0;
        var startSitCandidates = 0;
        var quickPickCandidates = 0;
        var quickPickPredictions = 0;
        var quickPickGraded = 0;
        var validProj = 0;
        var withOutcome = 0;
        var exclusions = Enum.GetValues<HistoricalCoverageExclusionReason>()
            .ToDictionary(r => r, _ => 0);

        for (var week = startWeek; week <= endWeek; week++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var raw = await _source
                .GetRawWeekAsync(season, week, ScoringType.Ppr, "nflverse", universe, cancellationToken)
                .ConfigureAwait(false);
            if (raw is null)
            {
                continue;
            }

            weeksLoaded++;
            var (snapshot, outcomes) = _builder.Build(raw);

            playerWeeks += snapshot.Players.Count;
            foreach (var p in snapshot.Players)
            {
                playerIds.Add(p.PlayerId);
            }

            startSitCandidates += snapshot.Roster.Count;
            validProj += snapshot.Players.Count(p => p.ProjectedPoints is not null);
            withOutcome += snapshot.Players.Count(p => outcomes.ByPlayerId.ContainsKey(p.PlayerId));

            var qpPreds = _quickPickGenerator.Generate(snapshot, QuickPickMode.Baseline);
            quickPickCandidates += CountPotentialQuickPickSlots(snapshot);
            quickPickPredictions += qpPreds.Count;
            quickPickGraded += qpPreds.Count(p =>
                outcomes.ByPlayerId.TryGetValue(p.PlayerId, out var o) &&
                ResolveActual(o, p.Market) is not null);

            TallyExclusions(snapshot, outcomes, exclusions);
        }

        // Start/Sit prediction counts via the existing season harness (formulas unchanged).
        var scorecard = await _seasonReplay.RunAsync(
                new MultiWeekReplayRequest
                {
                    Season = season,
                    StartWeek = startWeek,
                    EndWeek = endWeek,
                    ScoringType = ScoringType.Ppr,
                    FixtureId = "nflverse",
                    CandidateUniverse = universe,
                    ContinueOnWeekFailure = true
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new HistoricalCoverageSliceCounts
        {
            Universe = universe,
            WeeksLoaded = weeksLoaded,
            DistinctPlayers = playerIds.Count,
            PlayerWeeks = playerWeeks,
            StartSitCandidates = startSitCandidates,
            StartSitPredictions = scorecard.TotalDecisions,
            StartSitGradedPredictions = scorecard.CorrectDecisions + scorecard.IncorrectDecisions,
            QuickPickCandidates = quickPickCandidates,
            QuickPickPredictions = quickPickPredictions,
            QuickPickGradedPredictions = quickPickGraded,
            PlayersWithValidProjection = validProj,
            PlayersWithWeekOutcome = withOutcome,
            ExclusionsByReason = exclusions
        };
    }

    private static void TallyExclusions(
        HistoricalSnapshot snapshot,
        HistoricalWeekOutcomes outcomes,
        IDictionary<HistoricalCoverageExclusionReason, int> exclusions)
    {
        foreach (var player in snapshot.Players)
        {
            if (player.ProjectedPoints is null)
            {
                exclusions[HistoricalCoverageExclusionReason.NoValidProjection]++;
                if (player.DataSufficiency is DataSufficiency.Insufficient or null)
                {
                    exclusions[HistoricalCoverageExclusionReason.NoPriorRegGames]++;
                }
            }

            if (!outcomes.ByPlayerId.ContainsKey(player.PlayerId))
            {
                exclusions[HistoricalCoverageExclusionReason.NoWeekOutcome]++;
            }

            foreach (var market in MarketsFor(player.Position))
            {
                var projected = ResolveProjected(player, market);
                if (projected is null || projected <= 0)
                {
                    exclusions[HistoricalCoverageExclusionReason.NoPositiveMarketProjection]++;
                }
            }
        }
    }

    /// <summary>
    /// Annotate LabRoster OutsideLabRosterCap using the expanded player-week total.
    /// </summary>
    public static HistoricalCoverageSliceCounts WithLabCapExclusion(
        HistoricalCoverageSliceCounts lab,
        HistoricalCoverageSliceCounts expanded)
    {
        var map = lab.ExclusionsByReason.ToDictionary(kv => kv.Key, kv => kv.Value);
        map[HistoricalCoverageExclusionReason.OutsideLabRosterCap] =
            Math.Max(0, expanded.PlayerWeeks - lab.PlayerWeeks);
        return new HistoricalCoverageSliceCounts
        {
            Universe = lab.Universe,
            WeeksLoaded = lab.WeeksLoaded,
            DistinctPlayers = lab.DistinctPlayers,
            PlayerWeeks = lab.PlayerWeeks,
            StartSitCandidates = lab.StartSitCandidates,
            StartSitPredictions = lab.StartSitPredictions,
            StartSitGradedPredictions = lab.StartSitGradedPredictions,
            QuickPickCandidates = lab.QuickPickCandidates,
            QuickPickPredictions = lab.QuickPickPredictions,
            QuickPickGradedPredictions = lab.QuickPickGradedPredictions,
            PlayersWithValidProjection = lab.PlayersWithValidProjection,
            PlayersWithWeekOutcome = lab.PlayersWithWeekOutcome,
            ExclusionsByReason = map
        };
    }

    private static int CountPotentialQuickPickSlots(HistoricalSnapshot snapshot)
    {
        var n = 0;
        foreach (var player in snapshot.Players)
        {
            n += MarketsFor(player.Position).Count;
        }

        return n;
    }

    private static IReadOnlyList<PredictionMarketType> MarketsFor(Position position) =>
        position switch
        {
            Position.QB => [PredictionMarketType.PassingYards],
            Position.RB =>
            [
                PredictionMarketType.RushingYards,
                PredictionMarketType.ReceivingYards,
                PredictionMarketType.Receptions
            ],
            Position.WR or Position.TE =>
            [
                PredictionMarketType.ReceivingYards,
                PredictionMarketType.Receptions
            ],
            _ => []
        };

    private static double? ResolveProjected(HistoricalPlayerState player, PredictionMarketType market) =>
        market switch
        {
            PredictionMarketType.PassingYards => player.ProjectedPassYards,
            PredictionMarketType.PassingTouchdowns => player.ProjectedPassTouchdowns,
            PredictionMarketType.RushingYards => player.ProjectedRushYards,
            PredictionMarketType.ReceivingYards => player.ProjectedReceivingYards,
            PredictionMarketType.Receptions => player.ProjectedReceptions,
            _ => null
        };

    private static double? ResolveActual(HistoricalPlayerOutcome outcome, PredictionMarketType market) =>
        market switch
        {
            PredictionMarketType.PassingYards => outcome.ActualPassYards,
            PredictionMarketType.PassingTouchdowns => outcome.ActualPassTouchdowns,
            PredictionMarketType.RushingYards => outcome.ActualRushYards,
            PredictionMarketType.ReceivingYards => outcome.ActualReceivingYards,
            PredictionMarketType.Receptions => outcome.ActualReceptions,
            _ => null
        };
}
