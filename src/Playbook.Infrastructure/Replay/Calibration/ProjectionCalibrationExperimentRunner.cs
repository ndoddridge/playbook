using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playbook.Application.Replay;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Calibration;

/// <summary>
/// Experiment 1 orchestration: fit on development seasons only, freeze, then ONE holdout eval.
/// </summary>
public sealed class ProjectionCalibrationExperimentRunner
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ProjectionCalibrationExperimentRunner> _logger;

    public ProjectionCalibrationExperimentRunner(
        IServiceProvider services,
        ILogger<ProjectionCalibrationExperimentRunner> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task<ProjectionCalibrationExperimentReport> RunOfficialExperimentAsync(
        CancellationToken cancellationToken = default)
    {
        var developmentSeasons = FrozenProjectionCalibrationV2.DevelopmentSeasons.ToList();
        var holdout = FrozenProjectionCalibrationV2.HoldoutSeason;

        // 1) Collect development observations under V1 (never touch holdout).
        var state = _services.GetRequiredService<HistoricalProjectionExperimentState>();
        state.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV1;

        var seasonRunner = _services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
        var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();

        var developmentCards = new List<SeasonScorecard>();
        foreach (var season in developmentSeasons)
        {
            var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Experiment1: collecting V1 development season {Season}", season);
            var card = await seasonRunner.RunAsync(
                    new MultiWeekReplayRequest
                    {
                        Season = season,
                        StartWeek = 1,
                        EndWeek = end,
                        FixtureId = "nflverse",
                        ContinueOnWeekFailure = true
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            developmentCards.Add(card);
        }

        var observations = developmentCards
            .SelectMany(c => c.ProjectionEvaluations)
            .Where(p =>
                p.BaselineOpportunityAwarePoints is not null &&
                p.BaselineRecentAveragePoints is not null)
            .Select(p => new CalibrationObservation
            {
                Season = p.Season,
                Week = p.Week,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                V1Predicted = p.BaselineOpportunityAwarePoints!.Value,
                Actual = p.ActualPoints,
                BaselineAPredicted = p.BaselineRecentAveragePoints!.Value
            })
            .ToList();

        if (observations.Any(o => o.Season == holdout))
        {
            throw new InvalidOperationException("Holdout leaked into development observation set.");
        }

        // 2) LOOCV selection on development only.
        var selection = ProjectionCalibrationFitter.SelectAndFreeze(
            observations,
            developmentSeasons,
            holdout);

        // Sanity: frozen source constants must match the selected refit (within rounding).
        AssertFrozenMatches(selection.Frozen);

        // 3) Development decision impact: V1 vs V2 on pooled development seasons.
        var devImpact = await MeasureDecisionImpactAsync(
                developmentSeasons,
                scopeSeasonLabel: 0,
                cancellationToken)
            .ConfigureAwait(false);

        // 4) ONE official holdout evaluation (after freeze).
        _logger.LogInformation("Experiment1: official holdout {Season} — V1 then V2 (single pass each)", holdout);
        var holdoutImpact = await MeasureDecisionImpactAsync(
                [holdout],
                scopeSeasonLabel: holdout,
                cancellationToken)
            .ConfigureAwait(false);

        state.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV1;
        var holdoutEnd = await calendar.GetRegularSeasonEndWeekAsync(holdout, cancellationToken)
            .ConfigureAwait(false);
        var holdoutCard = await seasonRunner.RunAsync(
                new MultiWeekReplayRequest
                {
                    Season = holdout,
                    StartWeek = 1,
                    EndWeek = holdoutEnd,
                    FixtureId = "nflverse",
                    ContinueOnWeekFailure = true
                },
                cancellationToken)
            .ConfigureAwait(false);

        var holdoutFair = holdoutCard.ProjectionEvaluations
            .Where(p =>
                p.BaselineOpportunityAwarePoints is not null &&
                p.BaselineRecentAveragePoints is not null &&
                p.ProjectionV2Points is not null)
            .ToList();

        var holdoutV1 = Metrics("Projection V1", holdoutFair, p => p.BaselineOpportunityAwarePoints!.Value);
        var holdoutV2 = Metrics("Projection V2", holdoutFair, p => p.ProjectionV2Points!.Value);
        var holdoutA = Metrics("Baseline A", holdoutFair, p => p.BaselineRecentAveragePoints!.Value);

        var devV1 = new ProjectionModelMetrics
        {
            ModelLabel = "Projection V1",
            N = observations.Count,
            Mae = Math.Round(selection.PooledDevMaeV1, 2),
            Rmse = Rmse(observations, o => o.V1Predicted),
            Bias = Math.Round(selection.PooledDevBiasV1, 2)
        };
        var devV2 = new ProjectionModelMetrics
        {
            ModelLabel = "Projection V2",
            N = observations.Count,
            Mae = Math.Round(selection.PooledDevMaeV2, 2),
            Rmse = RmseLoo(observations, developmentSeasons, selection),
            Bias = Math.Round(selection.PooledDevBiasV2, 2)
        };
        var devA = new ProjectionModelMetrics
        {
            ModelLabel = "Baseline A",
            N = observations.Count,
            Mae = Math.Round(selection.PooledDevMaeBaselineA, 2),
            Rmse = Rmse(observations, o => o.BaselineAPredicted),
            Bias = Bias(observations, o => o.BaselineAPredicted)
        };

        var verdict = Judge(devV1, devV2, holdoutV1, holdoutV2, holdoutImpact);

        return new ProjectionCalibrationExperimentReport
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Hypothesis =
                "The current projection system systematically over-projects, and a simple " +
                "calibration layer can reduce projection error without overfitting.",
            MethodDescription = FrozenProjectionCalibrationV2.SelectionSummary +
                                $" LOOCV selected {selection.SelectedMethod}; frozen constants verified.",
            SuccessCriteriaText = ProjectionCalibrationSuccessCriteria.Text,
            DevelopmentSeasons = developmentSeasons,
            HoldoutSeason = holdout,
            SelectedMethod = selection.SelectedMethod,
            LooFoldSummaries = selection.Summaries,
            DevV1 = devV1,
            DevV2 = devV2,
            DevBaselineA = devA,
            HoldoutV1 = holdoutV1,
            HoldoutV2 = holdoutV2,
            HoldoutBaselineA = holdoutA,
            HoldoutDecisionImpact = holdoutImpact,
            DevelopmentDecisionImpact = devImpact,
            Verdict = verdict.Verdict,
            VerdictRationale = verdict.Rationale,
            UsedHoldoutDuringFitting = false
        };
    }

    private async Task<DecisionImpactReport> MeasureDecisionImpactAsync(
        IReadOnlyList<int> seasons,
        int scopeSeasonLabel,
        CancellationToken cancellationToken)
    {
        var state = _services.GetRequiredService<HistoricalProjectionExperimentState>();
        var seasonRunner = _services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
        var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();

        async Task<List<SeasonScorecard>> RunAll(HistoricalProjectionPrimaryMode mode)
        {
            state.PrimaryMode = mode;
            var cards = new List<SeasonScorecard>();
            foreach (var season in seasons)
            {
                var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
                    .ConfigureAwait(false);
                cards.Add(await seasonRunner.RunAsync(
                        new MultiWeekReplayRequest
                        {
                            Season = season,
                            StartWeek = 1,
                            EndWeek = end,
                            FixtureId = "nflverse",
                            ContinueOnWeekFailure = true
                        },
                        cancellationToken)
                    .ConfigureAwait(false));
            }

            return cards;
        }

        var v1Cards = await RunAll(HistoricalProjectionPrimaryMode.ProjectionV1).ConfigureAwait(false);
        var v2Cards = await RunAll(HistoricalProjectionPrimaryMode.ProjectionV2).ConfigureAwait(false);
        state.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV1;

        var g1 = v1Cards.SelectMany(c => c.AllGrades).ToList();
        var g2 = v2Cards.SelectMany(c => c.AllGrades).ToList();

        static string Key(ReplayDecisionGrade g) =>
            $"{g.Season}|{g.Week}|{g.PlayerId}|{g.Recommendation}";

        var map1 = g1.GroupBy(Key).ToDictionary(x => x.Key, x => x.First());
        var map2 = g2.GroupBy(Key).ToDictionary(x => x.Key, x => x.First());

        // Changed decisions: same player-week where recommendation differs OR expected value differs materially.
        // Compare by player-week pairing of Start recommendations primarily.
        var byPlayerWeek1 = g1.GroupBy(g => (g.Season, g.Week, g.PlayerId)).ToDictionary(x => x.Key, x => x.First());
        var byPlayerWeek2 = g2.GroupBy(g => (g.Season, g.Week, g.PlayerId)).ToDictionary(x => x.Key, x => x.First());

        var changed = 0;
        var improved = 0;
        var worsened = 0;
        var unchangedOutcome = 0;
        foreach (var key in byPlayerWeek1.Keys.Intersect(byPlayerWeek2.Keys))
        {
            var a = byPlayerWeek1[key];
            var b = byPlayerWeek2[key];
            var recChanged = a.Recommendation != b.Recommendation ||
                             Math.Abs(a.ExpectedValue - b.ExpectedValue) >= 0.05;
            if (!recChanged)
            {
                continue;
            }

            changed++;
            if (a.WasCorrect is null || b.WasCorrect is null ||
                a.ActualDecisionDifferential is null || b.ActualDecisionDifferential is null)
            {
                unchangedOutcome++;
                continue;
            }

            if (b.ActualDecisionDifferential.Value > a.ActualDecisionDifferential.Value + 0.05)
            {
                improved++;
            }
            else if (b.ActualDecisionDifferential.Value + 0.05 < a.ActualDecisionDifferential.Value)
            {
                worsened++;
            }
            else
            {
                unchangedOutcome++;
            }
        }

        _ = map1;
        _ = map2;

        return new DecisionImpactReport
        {
            ScopeSeason = scopeSeasonLabel,
            TotalDecisionsV1 = g1.Count,
            TotalDecisionsV2 = g2.Count,
            AccuracyV1 = Accuracy(g1),
            AccuracyV2 = Accuracy(g2),
            AverageDecisionValueV1 = AvgDiff(g1),
            AverageDecisionValueV2 = AvgDiff(g2),
            TotalDecisionValueV1 = SumDiff(g1),
            TotalDecisionValueV2 = SumDiff(g2),
            DecisionsChanged = changed,
            ChangedImproved = improved,
            ChangedWorsened = worsened,
            ChangedUnchangedOutcome = unchangedOutcome
        };
    }

    private static void AssertFrozenMatches(ProjectionCalibrationFitter.FittedCalibration frozen)
    {
        if (frozen.Method != FrozenProjectionCalibrationV2.Method)
        {
            throw new InvalidOperationException(
                $"Frozen method mismatch: fitter selected {frozen.Method} but " +
                $"FrozenProjectionCalibrationV2.Method={FrozenProjectionCalibrationV2.Method}. " +
                "Update frozen constants from a development-only refit before holdout.");
        }

        // Allow tiny float drift in committed constants.
        static bool Close(double a, double b) => Math.Abs(a - b) <= 0.02;
        if (!Close(frozen.HighSlope, FrozenProjectionCalibrationV2.HighSlope) ||
            !Close(frozen.HighIntercept, FrozenProjectionCalibrationV2.HighIntercept) ||
            !Close(frozen.LowSlope, FrozenProjectionCalibrationV2.LowSlope) ||
            !Close(frozen.LowIntercept, FrozenProjectionCalibrationV2.LowIntercept))
        {
            throw new InvalidOperationException(
                "Frozen calibration constants do not match development refit. " +
                $"Fit high=({frozen.HighIntercept:0.####},{frozen.HighSlope:0.####}) " +
                $"low=({frozen.LowIntercept:0.####},{frozen.LowSlope:0.####}) " +
                $"vs frozen high=({FrozenProjectionCalibrationV2.HighIntercept},{FrozenProjectionCalibrationV2.HighSlope}) " +
                $"low=({FrozenProjectionCalibrationV2.LowIntercept},{FrozenProjectionCalibrationV2.LowSlope}).");
        }
    }

    private static ProjectionModelMetrics Metrics(
        string label,
        IReadOnlyList<PlayerProjectionEvaluation> rows,
        Func<PlayerProjectionEvaluation, double> pred)
    {
        var preds = rows.Select(r => (Pred: pred(r), r.ActualPoints)).ToList();
        return new ProjectionModelMetrics
        {
            ModelLabel = label,
            N = preds.Count,
            Mae = preds.Count == 0 ? null : Math.Round(preds.Average(p => Math.Abs(p.ActualPoints - p.Pred)), 2),
            Rmse = preds.Count == 0
                ? null
                : Math.Round(Math.Sqrt(preds.Average(p => Math.Pow(p.ActualPoints - p.Pred, 2))), 2),
            Bias = preds.Count == 0 ? null : Math.Round(preds.Average(p => p.ActualPoints - p.Pred), 2)
        };
    }

    private static double? Rmse(IReadOnlyList<CalibrationObservation> rows, Func<CalibrationObservation, double> pred)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        return Math.Round(Math.Sqrt(rows.Average(o => Math.Pow(o.Actual - pred(o), 2))), 2);
    }

    private static double? RmseLoo(
        IReadOnlyList<CalibrationObservation> observations,
        IReadOnlyList<int> seasons,
        ProjectionCalibrationFitter.SelectionResult selection)
    {
        var sq = new List<double>();
        foreach (var season in seasons)
        {
            var train = observations.Where(o => o.Season != season).ToList();
            var val = observations.Where(o => o.Season == season).ToList();
            var fit = ProjectionCalibrationFitter.Fit(selection.SelectedMethod, train, 20);
            foreach (var o in val)
            {
                var p = ProjectionCalibrationFitter.Apply(fit, o.V1Predicted);
                sq.Add(Math.Pow(o.Actual - p, 2));
            }
        }

        return sq.Count == 0 ? null : Math.Round(Math.Sqrt(sq.Average()), 2);
    }

    private static double? Bias(IReadOnlyList<CalibrationObservation> rows, Func<CalibrationObservation, double> pred) =>
        rows.Count == 0 ? null : Math.Round(rows.Average(o => o.Actual - pred(o)), 2);

    private static double? Accuracy(IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var graded = grades.Where(g => g.WasCorrect is not null).ToList();
        return graded.Count == 0
            ? null
            : Math.Round(100.0 * graded.Count(g => g.WasCorrect == true) / graded.Count, 1);
    }

    private static double? AvgDiff(IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var diffs = grades.Where(g => g.ActualDecisionDifferential is not null)
            .Select(g => g.ActualDecisionDifferential!.Value).ToList();
        return diffs.Count == 0 ? null : Math.Round(diffs.Average(), 2);
    }

    private static double? SumDiff(IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var diffs = grades.Where(g => g.ActualDecisionDifferential is not null)
            .Select(g => g.ActualDecisionDifferential!.Value).ToList();
        return diffs.Count == 0 ? null : Math.Round(diffs.Sum(), 2);
    }

    private static (ProjectionExperimentVerdict Verdict, string Rationale) Judge(
        ProjectionModelMetrics devV1,
        ProjectionModelMetrics devV2,
        ProjectionModelMetrics holdoutV1,
        ProjectionModelMetrics holdoutV2,
        DecisionImpactReport holdoutDecisions)
    {
        if (devV1.Mae is null || devV2.Mae is null || holdoutV1.Mae is null || holdoutV2.Mae is null ||
            devV1.Bias is null || devV2.Bias is null || holdoutV1.Bias is null || holdoutV2.Bias is null)
        {
            return (ProjectionExperimentVerdict.Inconclusive, "Missing metrics.");
        }

        var devMaeImp = (devV1.Mae.Value - devV2.Mae.Value) / Math.Max(1e-9, devV1.Mae.Value);
        var devBiasImp = (Math.Abs(devV1.Bias.Value) - Math.Abs(devV2.Bias.Value)) /
                         Math.Max(1e-9, Math.Abs(devV1.Bias.Value));
        var holdMaeImp = (holdoutV1.Mae.Value - holdoutV2.Mae.Value) / Math.Max(1e-9, holdoutV1.Mae.Value);
        var holdBiasImp = (Math.Abs(holdoutV1.Bias.Value) - Math.Abs(holdoutV2.Bias.Value)) /
                          Math.Max(1e-9, Math.Abs(holdoutV1.Bias.Value));

        var decisionDelta =
            (holdoutDecisions.TotalDecisionValueV2 ?? 0) - (holdoutDecisions.TotalDecisionValueV1 ?? 0);

        var devOk = devMaeImp >= ProjectionCalibrationSuccessCriteria.MinRelativeMaeImprovementDev &&
                    devBiasImp >= ProjectionCalibrationSuccessCriteria.MinBiasMagnitudeReductionDev;
        var holdOk = holdMaeImp >= ProjectionCalibrationSuccessCriteria.MinRelativeMaeImprovementHoldout &&
                     holdBiasImp >= ProjectionCalibrationSuccessCriteria.MinBiasMagnitudeReductionHoldout;
        var decisionOk = decisionDelta >= -ProjectionCalibrationSuccessCriteria.MaxDecisionValueDegradationHoldout;

        if (holdMaeImp < 0 || holdBiasImp < 0)
        {
            return (
                ProjectionExperimentVerdict.Regression,
                $"Holdout worsened (MAE imp={holdMaeImp:P1}, |bias| imp={holdBiasImp:P1}). Keep V1.");
        }

        if (devOk && holdOk && decisionOk)
        {
            return (
                ProjectionExperimentVerdict.Improvement,
                $"Dev MAE imp={devMaeImp:P1}, |bias| imp={devBiasImp:P1}; " +
                $"Holdout MAE imp={holdMaeImp:P1}, |bias| imp={holdBiasImp:P1}; " +
                $"decision value Δ={decisionDelta:0.0}. Accept V2 as calibrated projection candidate.");
        }

        if (holdOk && !decisionOk)
        {
            return (
                ProjectionExperimentVerdict.Inconclusive,
                $"Holdout projection improved (MAE {holdMaeImp:P1}, |bias| {holdBiasImp:P1}) but " +
                $"decision value degraded by {-decisionDelta:0.0} (> " +
                $"{ProjectionCalibrationSuccessCriteria.MaxDecisionValueDegradationHoldout}). " +
                "Do not auto-accept.");
        }

        if (!devOk)
        {
            return (
                ProjectionExperimentVerdict.NoMaterialImprovement,
                $"Development LOOCV did not meet criteria (MAE imp={devMaeImp:P1}, |bias| imp={devBiasImp:P1}).");
        }

        return (
            ProjectionExperimentVerdict.NoMaterialImprovement,
            $"Holdout did not meet criteria (MAE imp={holdMaeImp:P1}, |bias| imp={holdBiasImp:P1}, " +
            $"decision Δ={decisionDelta:0.0}). Keep V1 as benchmark.");
    }
}
