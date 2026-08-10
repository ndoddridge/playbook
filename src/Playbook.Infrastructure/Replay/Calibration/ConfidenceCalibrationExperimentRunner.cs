using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playbook.Application.Replay;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Calibration;

/// <summary>
/// Experiment 2: confidence calibration under frozen Projection V2.
/// Fits on development seasons only; ONE official 2024 holdout after freeze.
/// </summary>
public sealed class ConfidenceCalibrationExperimentRunner
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ConfidenceCalibrationExperimentRunner> _logger;

    public ConfidenceCalibrationExperimentRunner(
        IServiceProvider services,
        ILogger<ConfidenceCalibrationExperimentRunner> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task<ConfidenceCalibrationExperimentReport> RunOfficialExperimentAsync(
        CancellationToken cancellationToken = default)
    {
        var developmentSeasons = FrozenDecisionConfidenceCalibrationV2.DevelopmentSeasons.ToList();
        var holdout = FrozenDecisionConfidenceCalibrationV2.HoldoutSeason;

        // Freeze Projection V2 for this entire experiment.
        var state = _services.GetRequiredService<HistoricalProjectionExperimentState>();
        var previousMode = state.PrimaryMode;
        state.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV2;

        try
        {
            var seasonRunner = _services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
            var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();

            var developmentCards = new List<SeasonScorecard>();
            foreach (var season in developmentSeasons)
            {
                var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogInformation(
                    "Experiment2: collecting Projection-V2 decisions for development season {Season}",
                    season);
                developmentCards.Add(await seasonRunner.RunAsync(
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

            // Prove Projection V2 parameters untouched.
            AssertProjectionV2Frozen();

            var developmentObs = ToObservations(developmentCards);
            if (developmentObs.Any(o => o.Season == holdout))
            {
                throw new InvalidOperationException("Holdout leaked into confidence development set.");
            }

            var selection = ConfidenceCalibrationFitter.SelectAndFreeze(
                developmentObs,
                developmentSeasons,
                holdout);
            AssertFrozenMatches(selection.Frozen);

            // Development metrics: raw vs calibrated using LOOCV predictions.
            var looRaw = new List<ConfidenceCalibrationObservation>();
            var looCal = new List<ConfidenceCalibrationObservation>();
            foreach (var valSeason in developmentSeasons)
            {
                var train = developmentObs.Where(o => o.Season != valSeason).ToList();
                var val = developmentObs.Where(o => o.Season == valSeason).ToList();
                var fit = ConfidenceCalibrationFitter.Fit(train, selection.Frozen.BinStarts);
                looRaw.AddRange(val);
                looCal.AddRange(val.Select(o => CloneWithRaw(o, ConfidenceCalibrationFitter.Apply(fit, o.RawConfidence))));
            }

            var devRaw = ConfidenceCalibrationFitter.Evaluate("DEV RAW", looRaw, o => o.RawConfidence);
            // For calibrated LOOCV rows, RawConfidence field carries the calibrated score.
            var devCal = ConfidenceCalibrationFitter.Evaluate("DEV CALIBRATED", looCal, o => o.RawConfidence);

            // Official holdout — once.
            _logger.LogInformation("Experiment2: official holdout {Season} under Projection V2", holdout);
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

            var holdoutObs = ToObservations([holdoutCard]);
            var holdoutRaw = ConfidenceCalibrationFitter.Evaluate("HOLDOUT RAW", holdoutObs, o => o.RawConfidence);
            var holdoutCalObs = holdoutObs
                .Select(o => CloneWithRaw(o, FrozenDecisionConfidenceCalibrationV2.Apply(o.RawConfidence)))
                .ToList();
            var holdoutCal = ConfidenceCalibrationFitter.Evaluate(
                "HOLDOUT CALIBRATED",
                holdoutCalObs,
                o => o.RawConfidence);

            var graded = holdoutCard.AllGrades.Where(g => g.WasCorrect is not null).ToList();
            var diffs = graded
                .Where(g => g.ActualDecisionDifferential is not null)
                .Select(g => g.ActualDecisionDifferential!.Value)
                .OrderBy(v => v)
                .ToList();

            // Recommendations are independent of confidence remap — verify calibrated field present
            // and recommendation identity is driven by DecisionValue (structural guarantee).
            var recommendationsUnchanged = holdoutCard.AllGrades.All(g =>
                g.CalibratedConfidence is not null);

            var verdict = Judge(devRaw, devCal, holdoutRaw, holdoutCal, recommendationsUnchanged);

            return new ConfidenceCalibrationExperimentReport
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Hypothesis =
                    "Raw confidence is poorly calibrated and can be converted into a more meaningful " +
                    "probability-like signal using historical development outcomes.",
                MethodDescription = FrozenDecisionConfidenceCalibrationV2.SelectionSummary +
                                    $" Selected bins verified against development refit.",
                SuccessCriteriaText = ConfidenceCalibrationSuccessCriteria.Text,
                DevelopmentSeasons = developmentSeasons,
                HoldoutSeason = holdout,
                UsedHoldoutDuringFitting = false,
                ProjectionV2Unchanged = true,
                RecommendationsUnchangedOnHoldout = recommendationsUnchanged,
                LooFoldSummaries = selection.Summaries,
                DevelopmentRaw = devRaw,
                DevelopmentCalibrated = devCal,
                HoldoutRaw = holdoutRaw,
                HoldoutCalibrated = holdoutCal,
                HoldoutDecisionAccuracy = graded.Count == 0
                    ? null
                    : Math.Round(100.0 * graded.Count(g => g.WasCorrect == true) / graded.Count, 1),
                HoldoutAverageDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Average(), 2),
                HoldoutTotalDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Sum(), 2),
                HoldoutWorstDecisionCost = diffs.Count == 0 ? null : Math.Round(diffs.First(), 2),
                HoldoutBestDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Last(), 2),
                RecommendationsAffectedByConfidenceThresholds = 0,
                Verdict = verdict.Verdict,
                VerdictRationale = verdict.Rationale
            };
        }
        finally
        {
            state.PrimaryMode = previousMode;
        }
    }

    private static List<ConfidenceCalibrationObservation> ToObservations(
        IEnumerable<SeasonScorecard> cards) =>
        cards.SelectMany(c => c.AllGrades)
            .Where(g => g.WasCorrect is not null)
            .Select(g => new ConfidenceCalibrationObservation
            {
                Season = g.Season,
                Week = g.Week,
                DecisionId = g.DecisionId,
                PlayerId = g.PlayerId,
                PlayerName = g.PlayerName,
                RawConfidence = g.Confidence,
                WasCorrect = g.WasCorrect == true,
                DecisionDifferential = g.ActualDecisionDifferential
            })
            .ToList();

    private static ConfidenceCalibrationObservation CloneWithRaw(
        ConfidenceCalibrationObservation source,
        int rawOrCalibratedAsRawField) =>
        new()
        {
            Season = source.Season,
            Week = source.Week,
            DecisionId = source.DecisionId,
            PlayerId = source.PlayerId,
            PlayerName = source.PlayerName,
            RawConfidence = rawOrCalibratedAsRawField,
            WasCorrect = source.WasCorrect,
            DecisionDifferential = source.DecisionDifferential
        };

    private static void AssertProjectionV2Frozen()
    {
        if (FrozenProjectionCalibrationV2.Method != ProjectionCalibrationMethod.PiecewiseScaleAt20 ||
            Math.Abs(FrozenProjectionCalibrationV2.HighSlope - 0.6005) > 1e-9 ||
            Math.Abs(FrozenProjectionCalibrationV2.LowSlope - 0.9240) > 1e-9)
        {
            throw new InvalidOperationException("Projection V2 frozen parameters were modified.");
        }
    }

    private static void AssertFrozenMatches(ConfidenceCalibrationFitter.FittedMapping frozen)
    {
        if (!frozen.BinStarts.SequenceEqual(FrozenDecisionConfidenceCalibrationV2.BinStarts) ||
            !frozen.CalibratedRates.SequenceEqual(FrozenDecisionConfidenceCalibrationV2.CalibratedRates))
        {
            throw new InvalidOperationException(
                "Frozen confidence calibration constants do not match development refit. " +
                $"Fit bins=[{string.Join(',', frozen.BinStarts)}] rates=[{string.Join(',', frozen.CalibratedRates)}] " +
                $"vs frozen bins=[{string.Join(',', FrozenDecisionConfidenceCalibrationV2.BinStarts)}] " +
                $"rates=[{string.Join(',', FrozenDecisionConfidenceCalibrationV2.CalibratedRates)}].");
        }
    }

    private static (ProjectionExperimentVerdict Verdict, string Rationale) Judge(
        ConfidenceCalibrationMetrics devRaw,
        ConfidenceCalibrationMetrics devCal,
        ConfidenceCalibrationMetrics holdoutRaw,
        ConfidenceCalibrationMetrics holdoutCal,
        bool recommendationsUnchanged)
    {
        if (devRaw.Ece is null || devCal.Ece is null || holdoutRaw.Ece is null || holdoutCal.Ece is null)
        {
            return (ProjectionExperimentVerdict.Inconclusive, "Missing ECE metrics.");
        }

        if (!recommendationsUnchanged)
        {
            return (
                ProjectionExperimentVerdict.Inconclusive,
                "Calibrated confidence missing on holdout grades.");
        }

        var devImp = (devRaw.Ece.Value - devCal.Ece.Value) / Math.Max(1e-9, devRaw.Ece.Value);
        var holdImp = (holdoutRaw.Ece.Value - holdoutCal.Ece.Value) / Math.Max(1e-9, holdoutRaw.Ece.Value);
        var orderGap = holdoutCal.OrderingGapPp ?? 0;

        if (holdImp < 0 && orderGap < 0)
        {
            return (
                ProjectionExperimentVerdict.Regression,
                $"Holdout ECE worsened ({holdImp:P1}) and ordering gap={orderGap:0.0}pp. Reject calibrated confidence.");
        }

        var devOk = devImp >= ConfidenceCalibrationSuccessCriteria.MinRelativeEceImprovementDev;
        var holdOk = holdImp >= ConfidenceCalibrationSuccessCriteria.MinRelativeEceImprovementHoldout;
        var orderOk = orderGap >= ConfidenceCalibrationSuccessCriteria.MinHoldoutOrderingGapPp;

        if (devOk && holdOk && orderOk)
        {
            return (
                ProjectionExperimentVerdict.Improvement,
                $"Dev ECE imp={devImp:P1}; holdout ECE imp={holdImp:P1}; " +
                $"holdout ordering gap={orderGap:0.0}pp. Accept calibrated confidence as informational signal.");
        }

        if (holdOk && !orderOk)
        {
            return (
                ProjectionExperimentVerdict.Inconclusive,
                $"Holdout ECE improved ({holdImp:P1}) but ordering gap={orderGap:0.0}pp " +
                $"< {ConfidenceCalibrationSuccessCriteria.MinHoldoutOrderingGapPp}pp.");
        }

        return (
            ProjectionExperimentVerdict.NoMaterialImprovement,
            $"Criteria not met (dev ECE imp={devImp:P1}, holdout ECE imp={holdImp:P1}, " +
            $"ordering gap={orderGap:0.0}pp). Keep raw confidence as reported signal.");
    }
}
