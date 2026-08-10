using Microsoft.Extensions.Logging;
using Playbook.Application.Knowledge;
using Playbook.Application.Predictions;
using Playbook.Application.Replay;
using Playbook.Core.Knowledge;
using Playbook.Core.Leagues;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;
namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Historical Quick Picks evaluation harness.
/// Baseline first, then Enhanced under AllowedEnhancedGroups (None in V1).
/// Development seasons before a single untouched 2024 holdout.
/// </summary>
public sealed class QuickPicksHistoricalEvaluationRunner : IQuickPicksHistoricalEvaluationRunner
{
    private readonly IHistoricalSnapshotSource _source;
    private readonly IHistoricalSnapshotBuilder _builder;
    private readonly IHistoricalSeasonCalendar _calendar;
    private readonly HistoricalQuickPickGenerator _generator;
    private readonly KnowledgeImpactExperimentState _knowledgeState;
    private readonly ILogger<QuickPicksHistoricalEvaluationRunner> _logger;

    public QuickPicksHistoricalEvaluationRunner(
        IHistoricalSnapshotSource source,
        IHistoricalSnapshotBuilder builder,
        IHistoricalSeasonCalendar calendar,
        HistoricalQuickPickGenerator generator,
        KnowledgeImpactExperimentState knowledgeState,
        ILogger<QuickPicksHistoricalEvaluationRunner> logger)
    {
        _source = source;
        _builder = builder;
        _calendar = calendar;
        _generator = generator;
        _knowledgeState = knowledgeState;
        _logger = logger;
    }

    public async Task<QuickPickSeasonScorecard> RunWeekAsync(
        int season,
        int week,
        QuickPickMode mode,
        string? fixtureId = "nflverse",
        ScoringType scoringType = ScoringType.Ppr,
        KnowledgeImpactGroup? enhancedGroups = null,
        HistoricalCandidateUniverse candidateUniverse = HistoricalCandidateUniverse.LabRoster,
        CancellationToken cancellationToken = default)
    {
        ConfigureMode(mode, enhancedGroups);
        var graded = await EvaluateWeekCoreAsync(
                season, week, scoringType, fixtureId, mode, candidateUniverse, cancellationToken)
            .ConfigureAwait(false);
        return QuickPickHistoricalGrader.BuildScorecard(
            season, mode, _knowledgeState.ActiveGroups, graded);
    }

    public async Task<QuickPickSeasonScorecard> RunSeasonAsync(
        int season,
        QuickPickMode mode,
        string? fixtureId = "nflverse",
        ScoringType scoringType = ScoringType.Ppr,
        KnowledgeImpactGroup? enhancedGroups = null,
        HistoricalCandidateUniverse candidateUniverse = HistoricalCandidateUniverse.LabRoster,
        CancellationToken cancellationToken = default)
    {
        ConfigureMode(mode, enhancedGroups);
        var end = await _calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
            .ConfigureAwait(false);

        var graded = new List<QuickPickGradedPrediction>();

        for (var week = 1; week <= end; week++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var weekGraded = await EvaluateWeekCoreAsync(
                    season, week, scoringType, fixtureId, mode, candidateUniverse, cancellationToken)
                .ConfigureAwait(false);
            if (weekGraded.Count == 0)
            {
                continue;
            }

            graded.AddRange(weekGraded);
        }

        var card = QuickPickHistoricalGrader.BuildScorecard(
            season, mode, _knowledgeState.ActiveGroups, graded);

        _logger.LogInformation(
            "QuickPicksHistEval: season {Season} mode={Mode} groups={Groups} weeks={Weeks}/{End} preds={Preds} MAE={Mae:0.000}",
            season, mode, _knowledgeState.ActiveGroups, card.WeeksEvaluated, end,
            card.PredictionsEvaluated, card.MeanAbsoluteError);

        return card;
    }

    public async Task<QuickPicksHistoricalEvaluationReport> RunOfficialEvaluationAsync(
        CancellationToken cancellationToken = default)
    {
        var previousMode = _knowledgeState.Mode;
        var previousGroups = _knowledgeState.ActiveGroups;

        try
        {
            var developmentSeasons = FrozenQuickPicksHistoricalEvaluationV1.DevelopmentSeasons.ToList();
            var holdout = FrozenQuickPicksHistoricalEvaluationV1.HoldoutSeason;

            // --- DEVELOPMENT (2024 must not be touched) ---
            _logger.LogInformation("QuickPicksHistEval: development BASELINE");
            var basDev = new List<QuickPickSeasonScorecard>();
            foreach (var season in developmentSeasons)
            {
                if (season == holdout)
                {
                    throw new InvalidOperationException("Holdout season leaked into development list.");
                }

                basDev.Add(await RunSeasonAsync(season, QuickPickMode.Baseline, cancellationToken: cancellationToken)
                    .ConfigureAwait(false));
            }

            _logger.LogInformation(
                "QuickPicksHistEval: development ENHANCED groups={Groups}",
                FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups);
            var enhDev = new List<QuickPickSeasonScorecard>();
            foreach (var season in developmentSeasons)
            {
                enhDev.Add(await RunSeasonAsync(season, QuickPickMode.Enhanced, cancellationToken: cancellationToken)
                    .ConfigureAwait(false));
            }

            var devChange = QuickPickHistoricalGrader.AnalyzeChanges(basDev, enhDev);

            // Freeze evaluator configuration before holdout.
            AssertEvaluatorFrozen();

            // --- OFFICIAL HOLDOUT (exactly once) ---
            _logger.LogInformation("QuickPicksHistEval: official holdout {Season} BASELINE", holdout);
            var basHold = await RunSeasonAsync(holdout, QuickPickMode.Baseline, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("QuickPicksHistEval: official holdout {Season} ENHANCED", holdout);
            var enhHold = await RunSeasonAsync(holdout, QuickPickMode.Enhanced, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var holdChange = QuickPickHistoricalGrader.AnalyzeChanges([basHold], [enhHold]);

            var rejectedReenabled =
                FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups.HasFlag(KnowledgeImpactGroup.Usage) ||
                FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups.HasFlag(KnowledgeImpactGroup.RoleHealth) ||
                FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups.HasFlag(KnowledgeImpactGroup.RecentForm) ||
                FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups.HasFlag(KnowledgeImpactGroup.Matchup);

            var verdict = BuildVerdict(devChange, holdChange, rejectedReenabled);

            return new QuickPicksHistoricalEvaluationReport
            {
                EvaluationId = FrozenQuickPicksHistoricalEvaluationV1.EvaluationId,
                EvaluatorVersion = FrozenQuickPicksHistoricalEvaluationV1.EvaluatorVersion,
                DevelopmentSeasons = developmentSeasons,
                HoldoutSeason = holdout,
                AllowedEnhancedGroups = FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups,
                SelectionSummary = FrozenQuickPicksHistoricalEvaluationV1.SelectionSummary,
                UsedHoldoutDuringDevelopment = false,
                RejectedKnowledgeTransformsReenabled = rejectedReenabled,
                ProjectionV2Unchanged = ProjectionLayersUnchanged(),
                ConfidenceV2Unchanged = ConfidenceLayersUnchanged(),
                DecisionPolicyV1Unchanged = DecisionPolicyUnchanged(),
                DevelopmentBaseline = basDev,
                DevelopmentEnhanced = enhDev,
                HoldoutBaseline = basHold,
                HoldoutEnhanced = enhHold,
                DevelopmentChangeAnalysis = devChange,
                HoldoutChangeAnalysis = holdChange,
                Verdict = verdict
            };
        }
        finally
        {
            _knowledgeState.Mode = previousMode;
            _knowledgeState.ActiveGroups = previousGroups;
            // Default production remains Passthrough.
            _knowledgeState.ConfigurePassthrough();
        }
    }

    public async Task<QuickPicksHistoricalEvaluationReport> RunOfficialRecentFormExperimentAsync(
        CancellationToken cancellationToken = default)
    {
        var previousMode = _knowledgeState.Mode;
        var previousGroups = _knowledgeState.ActiveGroups;
        var groups = FrozenQuickPicksRecentFormExperimentV1.ExperimentalGroups;

        try
        {
            AssertRecentFormExperimentFrozen();

            var developmentSeasons = FrozenQuickPicksRecentFormExperimentV1.DevelopmentSeasons.ToList();
            var holdout = FrozenQuickPicksRecentFormExperimentV1.HoldoutSeason;

            _logger.LogInformation("QuickPicksRecentForm: development BASELINE");
            var basDev = new List<QuickPickSeasonScorecard>();
            foreach (var season in developmentSeasons)
            {
                if (season == holdout)
                {
                    throw new InvalidOperationException("Holdout season leaked into development list.");
                }

                basDev.Add(await RunSeasonAsync(
                        season, QuickPickMode.Baseline, cancellationToken: cancellationToken)
                    .ConfigureAwait(false));
            }

            // Determinism check: re-run first development season Baseline and require identity.
            var basDevRepeat = await RunSeasonAsync(
                    developmentSeasons[0], QuickPickMode.Baseline, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            AssertScorecardIdentical(basDev[0], basDevRepeat, "development Baseline determinism");

            _logger.LogInformation("QuickPicksRecentForm: development ENHANCED RecentForm");
            var enhDev = new List<QuickPickSeasonScorecard>();
            foreach (var season in developmentSeasons)
            {
                enhDev.Add(await RunSeasonAsync(
                        season, QuickPickMode.Enhanced, enhancedGroups: groups,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false));
            }

            var enhDevRepeat = await RunSeasonAsync(
                    developmentSeasons[0], QuickPickMode.Enhanced, enhancedGroups: groups,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            AssertScorecardIdentical(enhDev[0], enhDevRepeat, "development Enhanced RecentForm determinism");

            var devChange = QuickPickHistoricalGrader.AnalyzeChanges(basDev, enhDev);

            // Freeze before holdout — no further config changes.
            AssertRecentFormExperimentFrozen();

            _logger.LogInformation("QuickPicksRecentForm: official holdout {Season} BASELINE", holdout);
            var basHold = await RunSeasonAsync(
                    holdout, QuickPickMode.Baseline, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("QuickPicksRecentForm: official holdout {Season} ENHANCED RecentForm", holdout);
            var enhHold = await RunSeasonAsync(
                    holdout, QuickPickMode.Enhanced, enhancedGroups: groups,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var holdChange = QuickPickHistoricalGrader.AnalyzeChanges([basHold], [enhHold]);

            var rejectedReenabled =
                groups.HasFlag(KnowledgeImpactGroup.Usage) ||
                groups.HasFlag(KnowledgeImpactGroup.RoleHealth) ||
                groups.HasFlag(KnowledgeImpactGroup.Matchup);

            var onlyRecentForm =
                groups == KnowledgeImpactGroup.RecentForm;

            var verdict = BuildRecentFormVerdict(devChange, holdChange, rejectedReenabled, onlyRecentForm);

            return new QuickPicksHistoricalEvaluationReport
            {
                EvaluationId = FrozenQuickPicksRecentFormExperimentV1.ExperimentId,
                EvaluatorVersion = FrozenQuickPicksHistoricalEvaluationV1.EvaluatorVersion,
                DevelopmentSeasons = developmentSeasons,
                HoldoutSeason = holdout,
                AllowedEnhancedGroups = groups,
                SelectionSummary =
                    FrozenQuickPicksRecentFormExperimentV1.Hypothesis + " " +
                    FrozenQuickPicksRecentFormExperimentV1.MappingSummary,
                UsedHoldoutDuringDevelopment = false,
                RejectedKnowledgeTransformsReenabled = rejectedReenabled,
                ProjectionV2Unchanged = ProjectionLayersUnchanged(),
                ConfidenceV2Unchanged = ConfidenceLayersUnchanged(),
                DecisionPolicyV1Unchanged = DecisionPolicyUnchanged(),
                DevelopmentBaseline = basDev,
                DevelopmentEnhanced = enhDev,
                HoldoutBaseline = basHold,
                HoldoutEnhanced = enhHold,
                DevelopmentChangeAnalysis = devChange,
                HoldoutChangeAnalysis = holdChange,
                Verdict = verdict
            };
        }
        finally
        {
            _knowledgeState.Mode = previousMode;
            _knowledgeState.ActiveGroups = previousGroups;
            _knowledgeState.ConfigurePassthrough();
        }
    }

    private async Task<IReadOnlyList<QuickPickGradedPrediction>> EvaluateWeekCoreAsync(
        int season,
        int week,
        ScoringType scoringType,
        string? fixtureId,
        QuickPickMode mode,
        HistoricalCandidateUniverse candidateUniverse,
        CancellationToken cancellationToken)
    {
        var raw = await _source
            .GetRawWeekAsync(season, week, scoringType, fixtureId, candidateUniverse, cancellationToken)
            .ConfigureAwait(false);
        if (raw is null)
        {
            return [];
        }

        var (snapshot, outcomes) = _builder.Build(raw);

        // Temporal integrity: predictions finalized before outcomes are consulted.
        AssertNoOutcomeLeakIntoSnapshot(snapshot, outcomes);

        var predictions = _generator.Generate(snapshot, mode);

        // Hard cutoff: knowledge contexts must not contain future evidence.
        foreach (var p in predictions.Where(p => p.KnowledgeContext is not null))
        {
            KnowledgeTemporalGuard.AssertNoFutureLeak(
                p.KnowledgeContext!.Knowledge,
                snapshot.InformationCutoff);

            if (p.CutoffTimestamp > snapshot.InformationCutoff)
            {
                throw new InvalidOperationException(
                    $"Prediction cutoff after snapshot cutoff for {p.PlayerName} {p.Market}.");
            }
        }

        // Outcomes attached only after predictions are finalized.
        return QuickPickHistoricalGrader.Grade(predictions, outcomes);
    }

    private void ConfigureMode(QuickPickMode mode, KnowledgeImpactGroup? enhancedGroups)
    {
        if (mode == QuickPickMode.Baseline)
        {
            _knowledgeState.ConfigureBaseline();
            return;
        }

        // Enhanced: experiment override when provided; otherwise frozen QP evaluation policy.
        var groups = enhancedGroups ?? FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups;
        _knowledgeState.ConfigureEnhanced(groups);
    }

    private static void AssertRecentFormExperimentFrozen()
    {
        if (FrozenQuickPicksRecentFormExperimentV1.ExperimentalGroups != KnowledgeImpactGroup.RecentForm)
        {
            throw new InvalidOperationException(
                "RecentForm experiment groups mutated — must remain RecentForm only.");
        }

        if (FrozenQuickPicksRecentFormExperimentV1.HighThreshold != 65 ||
            FrozenQuickPicksRecentFormExperimentV1.LowThreshold != 35 ||
            Math.Abs(FrozenQuickPicksRecentFormExperimentV1.QuickPickOpportunityDelta - 0.6) > 1e-9)
        {
            throw new InvalidOperationException("RecentForm thresholds/delta retuned before holdout.");
        }

        if (FrozenQuickPicksRecentFormExperimentV1.HoldoutSeason != 2024)
        {
            throw new InvalidOperationException("Holdout season mutated.");
        }

        // Harness V1 freeze must remain observational (None) — this experiment overrides per-call.
        if (FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups != KnowledgeImpactGroup.None)
        {
            throw new InvalidOperationException(
                "QP evaluation V1 AllowedEnhancedGroups mutated — keep None; experiment uses override.");
        }
    }

    private static void AssertScorecardIdentical(
        QuickPickSeasonScorecard a,
        QuickPickSeasonScorecard b,
        string label)
    {
        if (a.PredictionsEvaluated != b.PredictionsEvaluated ||
            Math.Abs(a.MeanAbsoluteError - b.MeanAbsoluteError) > 1e-9 ||
            Math.Abs(a.Top5HitRate - b.Top5HitRate) > 1e-9 ||
            Math.Abs(a.TotalPredictionValue - b.TotalPredictionValue) > 1e-9)
        {
            throw new InvalidOperationException($"Non-deterministic Quick Picks replay ({label}).");
        }

        var aKeys = a.Graded.Select(g =>
            (g.Prediction.PlayerId, g.Prediction.Market, g.Prediction.RankingScore, g.Prediction.RankInMarket));
        var bKeys = b.Graded.Select(g =>
            (g.Prediction.PlayerId, g.Prediction.Market, g.Prediction.RankingScore, g.Prediction.RankInMarket));
        if (!aKeys.SequenceEqual(bKeys))
        {
            throw new InvalidOperationException($"Non-deterministic graded predictions ({label}).");
        }
    }

    private static string BuildRecentFormVerdict(
        QuickPickChangeAnalysis dev,
        QuickPickChangeAnalysis hold,
        bool rejectedReenabled,
        bool onlyRecentForm)
    {
        if (rejectedReenabled || !onlyRecentForm)
        {
            return "INVALID — experiment must isolate RecentForm and keep rejected transforms disabled.";
        }

        var maeDelta = hold.BaselineMeanAbsoluteError - hold.EnhancedMeanAbsoluteError; // + = improvement
        var top5Delta = hold.EnhancedTop5HitRate - hold.BaselineTop5HitRate; // + = improvement
        var rankChangeRate = hold.PercentRanksChanged;

        if (hold.PredictionsIdentical ||
            rankChangeRate < QuickPicksRecentFormVerdictRules.MinMaterialRankChangeRatePercent)
        {
            return "NEUTRAL — RecentForm did not materially change holdout Quick Pick ranks " +
                   $"(rankChangeRate={rankChangeRate:0.00}%, scoreChangeRate={hold.PercentChanged:0.00}%, " +
                   $"MAE Δ={maeDelta:0.000}, Top5 Δ={top5Delta:0.0}pp). " +
                   "Recommendation: DISABLED. Production default remains Passthrough.";
        }

        var maeImproved = maeDelta >= QuickPicksRecentFormVerdictRules.MinHoldoutMaeImprovement;
        var top5Improved = top5Delta >= QuickPicksRecentFormVerdictRules.MinHoldoutTop5ImprovementPp;
        var top5Regressed = top5Delta <= -QuickPicksRecentFormVerdictRules.MinHoldoutTop5RegressionPp;
        var maeRegressed = maeDelta <= -QuickPicksRecentFormVerdictRules.MinHoldoutMaeRegression;

        if ((maeImproved || top5Improved) && !top5Regressed && !maeRegressed)
        {
            return "IMPROVEMENT — RecentForm improved holdout Quick Picks with material rank changes " +
                   $"(MAE Δ={maeDelta:0.000}, Top5 Δ={top5Delta:0.0}pp, rankChangeRate={rankChangeRate:0.00}%). " +
                   "Recommendation: ENABLED (behind experiment mode; production default still unchanged until accepted).";
        }

        if (maeRegressed || top5Regressed)
        {
            return "REGRESSION — RecentForm worsened holdout Quick Picks " +
                   $"(MAE Δ={maeDelta:0.000}, Top5 Δ={top5Delta:0.0}pp, rankChangeRate={rankChangeRate:0.00}%). " +
                   "Recommendation: DISABLED.";
        }

        return "NEUTRAL — holdout differences were negligible under verdict rules " +
               $"(MAE Δ={maeDelta:0.000}, Top5 Δ={top5Delta:0.0}pp, rankChangeRate={rankChangeRate:0.00}%, " +
               $"scoreChangeRate={hold.PercentChanged:0.00}%). " +
               $"Dev rankChangeRate={dev.PercentRanksChanged:0.00}% (informational only). " +
               "Recommendation: DISABLED.";
    }

    private static void AssertNoOutcomeLeakIntoSnapshot(
        HistoricalSnapshot snapshot,
        HistoricalWeekOutcomes outcomes)
    {
        // Snapshot must not embed actual fantasy/counting outcomes.
        foreach (var player in snapshot.Players)
        {
            if (outcomes.ByPlayerId.TryGetValue(player.PlayerId, out var outcome))
            {
                // Projected counting stats may exist; actuals must remain only on outcomes.
                if (player.ProjectedPoints is decimal proj &&
                    Math.Abs((double)proj - outcome.ActualFantasyPoints) < 1e-9 &&
                    player.UnavailableSignals.Any(s =>
                        s.Contains(outcome.ActualFantasyPoints.ToString("0.0"), StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        $"Suspicious outcome leak into snapshot signals for {player.PlayerName}.");
                }
            }
        }
    }

    private static void AssertEvaluatorFrozen()
    {
        if (FrozenQuickPicksHistoricalEvaluationV1.EvaluatorVersion != "qp-hist-eval-v1")
        {
            throw new InvalidOperationException("Evaluator version mutated before holdout.");
        }

        if (FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups != KnowledgeImpactGroup.None)
        {
            throw new InvalidOperationException(
                "AllowedEnhancedGroups changed before holdout — rejected transforms must stay off.");
        }

        if (FrozenQuickPicksHistoricalEvaluationV1.HoldoutSeason != 2024)
        {
            throw new InvalidOperationException("Holdout season mutated.");
        }
    }

    private static bool ProjectionLayersUnchanged() =>
        FrozenProjectionCalibrationV2.Method == ProjectionCalibrationMethod.PiecewiseScaleAt20 &&
        Math.Abs(FrozenProjectionCalibrationV2.HighSlope - 0.6005) < 1e-9 &&
        Math.Abs(FrozenProjectionCalibrationV2.LowSlope - 0.9240) < 1e-9;

    private static bool ConfidenceLayersUnchanged() =>
        FrozenDecisionConfidenceCalibrationV2.BinStarts.SequenceEqual(new[] { 0, 15, 25, 35 }) &&
        FrozenDecisionConfidenceCalibrationV2.CalibratedRates.SequenceEqual(new[] { 57, 67, 65, 42 });

    private static bool DecisionPolicyUnchanged() =>
        FrozenConfidenceAwareDecisionPolicyV1.Kind == DecisionPolicyKinds.SuppressStartAndSit &&
        FrozenConfidenceAwareDecisionPolicyV1.MaxCalibratedConfidenceToSuppressStart == 45;

    private static string BuildVerdict(
        QuickPickChangeAnalysis dev,
        QuickPickChangeAnalysis hold,
        bool rejectedReenabled)
    {
        if (rejectedReenabled)
        {
            return "INVALID — rejected knowledge transforms were re-enabled.";
        }

        if (dev.PredictionsIdentical && hold.PredictionsIdentical)
        {
            return "BASELINE ESTABLISHED — Enhanced identical to Baseline " +
                   "(AllowedEnhancedGroups=None / observational). " +
                   "No Quick Picks knowledge improvement is claimed. " +
                   "Harness is ready for future knowledge experiments.";
        }

        var maeDelta = hold.BaselineMeanAbsoluteError - hold.EnhancedMeanAbsoluteError;
        if (maeDelta > 0.5 && hold.PercentChanged >= 5)
        {
            return "IMPROVEMENT — Enhanced reduced holdout MAE with material prediction changes.";
        }

        if (maeDelta < -0.5 && hold.PercentChanged >= 5)
        {
            return "REGRESSION — Enhanced worsened holdout MAE.";
        }

        return "NO MATERIAL IMPROVEMENT — Enhanced differed but did not clearly improve holdout grading.";
    }
}
