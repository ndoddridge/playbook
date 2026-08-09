using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playbook.Application.Replay;
using Playbook.Core.Decisions;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Calibration;

/// <summary>
/// Experiment 3: confidence-aware decision policy under frozen Projection V2 + Confidence V2.
/// Fits on development seasons only; ONE official 2024 holdout after freeze.
/// </summary>
public sealed class ConfidenceAwareDecisionPolicyExperimentRunner
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ConfidenceAwareDecisionPolicyExperimentRunner> _logger;

    public ConfidenceAwareDecisionPolicyExperimentRunner(
        IServiceProvider services,
        ILogger<ConfidenceAwareDecisionPolicyExperimentRunner> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// Development-only LOOCV selection (no holdout). Used to freeze constants before the official run.
    /// </summary>
    public async Task<ConfidenceAwareDecisionPolicyFitter.SelectionResult> RunDevelopmentSelectionAsync(
        CancellationToken cancellationToken = default)
    {
        var developmentSeasons = FrozenConfidenceAwareDecisionPolicyV1.DevelopmentSeasons.ToList();
        var holdout = FrozenConfidenceAwareDecisionPolicyV1.HoldoutSeason;

        var projectionState = _services.GetRequiredService<HistoricalProjectionExperimentState>();
        var policyState = _services.GetRequiredService<ConfidenceAwareDecisionPolicyState>();
        var previousProjection = projectionState.PrimaryMode;
        var previousPolicy = policyState.Mode;
        projectionState.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV2;
        policyState.Mode = ConfidenceAwareDecisionPolicyMode.Off;

        try
        {
            var obs = await CollectDevelopmentObservationsAsync(developmentSeasons, holdout, cancellationToken)
                .ConfigureAwait(false);
            return ConfidenceAwareDecisionPolicyFitter.SelectViaLeaveOneSeasonOut(
                obs,
                developmentSeasons,
                holdout);
        }
        finally
        {
            projectionState.PrimaryMode = previousProjection;
            policyState.Mode = previousPolicy;
        }
    }

    public async Task<ConfidenceAwareDecisionPolicyExperimentReport> RunOfficialExperimentAsync(
        CancellationToken cancellationToken = default)
    {
        var developmentSeasons = FrozenConfidenceAwareDecisionPolicyV1.DevelopmentSeasons.ToList();
        var holdout = FrozenConfidenceAwareDecisionPolicyV1.HoldoutSeason;

        var projectionState = _services.GetRequiredService<HistoricalProjectionExperimentState>();
        var policyState = _services.GetRequiredService<ConfidenceAwareDecisionPolicyState>();
        var previousProjection = projectionState.PrimaryMode;
        var previousPolicy = policyState.Mode;

        // Freeze Projection V2; policy Off while collecting control observations.
        projectionState.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV2;
        policyState.Mode = ConfidenceAwareDecisionPolicyMode.Off;

        try
        {
            AssertProjectionV2Frozen();
            AssertConfidenceV2Frozen();

            var developmentObs = await CollectDevelopmentObservationsAsync(
                    developmentSeasons,
                    holdout,
                    cancellationToken)
                .ConfigureAwait(false);

            var selection = ConfidenceAwareDecisionPolicyFitter.SelectViaLeaveOneSeasonOut(
                developmentObs,
                developmentSeasons,
                holdout);

            AssertFrozenMatchesSelected(selection.Selected);

            var seasonRunner = _services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
            var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();

            var selected = selection.Selected;
            var devControl = ConfidenceAwareDecisionPolicyFitter.Evaluate("DEV CONTROL", developmentObs, null);
            var devExperiment = ConfidenceAwareDecisionPolicyFitter.Evaluate(
                "DEV EXPERIMENT",
                developmentObs,
                selected);

            // Official holdout — control once, then experiment once.
            _logger.LogInformation("Experiment3: official holdout {Season} CONTROL (policy Off)", holdout);
            policyState.Mode = ConfidenceAwareDecisionPolicyMode.Off;
            var holdoutEnd = await calendar.GetRegularSeasonEndWeekAsync(holdout, cancellationToken)
                .ConfigureAwait(false);
            var holdoutControlCard = await seasonRunner.RunAsync(
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

            var holdoutControlObs = holdoutControlCard.AllGrades
                .Where(g => g.WasCorrect is not null)
                .Select(ConfidenceAwareDecisionPolicyFitter.FromGrade)
                .ToList();
            var holdoutControl = ConfidenceAwareDecisionPolicyFitter.Evaluate(
                "HOLDOUT CONTROL",
                holdoutControlObs,
                null);

            _logger.LogInformation("Experiment3: official holdout {Season} EXPERIMENT (policy On)", holdout);
            policyState.Mode = ConfidenceAwareDecisionPolicyMode.On;
            var holdoutExperimentCard = await seasonRunner.RunAsync(
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

            var holdoutExperimentObs = holdoutExperimentCard.AllGrades
                .Where(g => g.WasCorrect is not null)
                .Select(ConfidenceAwareDecisionPolicyFitter.FromGrade)
                .ToList();

            // Live On run already applied policy; metrics from remaining graded decisions.
            // Also compute offline simulation on control for suppressedWouldHave / counts parity.
            var holdoutSim = ConfidenceAwareDecisionPolicyFitter.Evaluate(
                "HOLDOUT EXPERIMENT (sim)",
                holdoutControlObs,
                selected);

            var holdoutExperiment = MetricsFromLiveHoldout(
                "HOLDOUT EXPERIMENT",
                holdoutExperimentObs,
                holdoutControl.Opportunities,
                holdoutSim);

            var failureNotes = BuildFailureAnalysis(
                holdoutControlObs,
                selected,
                holdoutControl,
                holdoutExperiment);

            var verdict = Judge(
                selection.MeanValidationTotalValueDelta,
                holdoutControl,
                holdoutExperiment);

            return new ConfidenceAwareDecisionPolicyExperimentReport
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Hypothesis =
                    "Calibrated confidence can improve actual fantasy decision quality by acting as a " +
                    "trust signal — suppressing low-trust marginal Starts without gaming abstention.",
                ControlDescription =
                    "Projection V2 + Calibrated Confidence V2 + existing DecisionEngine Start/Sit rules (policy Off).",
                ExperimentalPolicyDescription =
                    selected.Description + " " + FrozenConfidenceAwareDecisionPolicyV1.SelectionSummary,
                SuccessCriteriaText = ConfidenceAwareDecisionPolicySuccessCriteria.Text,
                DevelopmentSeasons = developmentSeasons,
                HoldoutSeason = holdout,
                UsedHoldoutDuringFitting = false,
                ProjectionV2Unchanged = true,
                ConfidenceV2Unchanged = true,
                LooFoldSummaries = selection.FoldSummaries
                    .Append(
                        $"selected={selected.CandidateId} meanValΔ={selection.MeanValidationTotalValueDelta:0.00} " +
                        $"meanRet={selection.MeanValidationRetention:0%}")
                    .ToList(),
                SelectedCandidateId = selected.CandidateId,
                DevelopmentControl = devControl,
                DevelopmentExperiment = devExperiment,
                HoldoutControl = holdoutControl,
                HoldoutExperiment = holdoutExperiment,
                FailureAnalysisNotes = failureNotes,
                Verdict = verdict.Verdict,
                VerdictRationale = verdict.Rationale
            };
        }
        finally
        {
            projectionState.PrimaryMode = previousProjection;
            policyState.Mode = previousPolicy;
        }
    }

    private async Task<List<DecisionPolicyObservation>> CollectDevelopmentObservationsAsync(
        IReadOnlyList<int> developmentSeasons,
        int holdout,
        CancellationToken cancellationToken)
    {
        var seasonRunner = _services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
        var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();
        var developmentCards = new List<SeasonScorecard>();
        foreach (var season in developmentSeasons)
        {
            var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Experiment3: collecting Projection-V2 control decisions for development season {Season}",
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

        var developmentObs = developmentCards
            .SelectMany(c => c.AllGrades)
            .Where(g => g.WasCorrect is not null)
            .Select(ConfidenceAwareDecisionPolicyFitter.FromGrade)
            .ToList();

        if (developmentObs.Any(o => o.Season == holdout))
        {
            throw new InvalidOperationException("Holdout leaked into decision-policy development set.");
        }

        return developmentObs;
    }

    private static DecisionPolicyScopeMetrics MetricsFromLiveHoldout(
        string label,
        IReadOnlyList<DecisionPolicyObservation> liveKept,
        int controlOpportunities,
        DecisionPolicyScopeMetrics sim)
    {
        var graded = liveKept.Where(o => o.WasCorrect is not null).ToList();
        var diffs = graded
            .Where(o => o.ActualDecisionDifferential is not null)
            .Select(o => o.ActualDecisionDifferential!.Value)
            .OrderBy(v => v)
            .ToList();

        return new DecisionPolicyScopeMetrics
        {
            Label = label,
            GradedDecisions = graded.Count,
            Opportunities = controlOpportunities,
            SuppressedStarts = sim.SuppressedStarts,
            SuppressedSits = sim.SuppressedSits,
            SwappedStarts = sim.SwappedStarts,
            LowTrustLabeled = sim.LowTrustLabeled,
            AccuracyPercent = graded.Count == 0
                ? null
                : Math.Round(100.0 * graded.Count(g => g.WasCorrect == true) / graded.Count, 1),
            AverageDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Average(), 2),
            TotalDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Sum(), 2),
            WorstDecisionCost = diffs.Count == 0 ? null : Math.Round(diffs.First(), 2),
            BestDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Last(), 2),
            SuppressedWouldHaveBeenTotalValue = sim.SuppressedWouldHaveBeenTotalValue,
            ConfidenceDistribution = ConfidenceAwareDecisionPolicyFitter.BuildConfidenceDistribution(liveKept)
        };
    }

    private static IReadOnlyList<string> BuildFailureAnalysis(
        IReadOnlyList<DecisionPolicyObservation> controlObs,
        DecisionPolicyCandidate policy,
        DecisionPolicyScopeMetrics control,
        DecisionPolicyScopeMetrics experiment)
    {
        var notes = new List<string>();
        var acted = controlObs.Where(o => ConfidenceAwareDecisionPolicyFitter.ShouldAct(o, policy)).ToList();
        var suppressedStarts = acted.Where(o => o.Recommendation == DecisionRecommendation.Start).ToList();

        notes.Add(
            $"Control total decision value={control.TotalDecisionValue:0.00}; " +
            $"experiment={experiment.TotalDecisionValue:0.00}; " +
            $"Δ={(experiment.TotalDecisionValue ?? 0) - (control.TotalDecisionValue ?? 0):0.00}.");

        notes.Add(
            $"Policy acted on {acted.Count}/{controlObs.Count} control graded decisions " +
            $"(starts={suppressedStarts.Count}).");

        if (suppressedStarts.Count > 0)
        {
            var byPos = suppressedStarts
                .GroupBy(o => o.Position)
                .Select(g =>
                {
                    var diffs = g.Where(x => x.ActualDecisionDifferential is not null)
                        .Select(x => x.ActualDecisionDifferential!.Value).ToList();
                    return $"{g.Key}:n={g.Count()} wouldHaveTot={(diffs.Count == 0 ? 0 : diffs.Sum()):0.0}";
                });
            notes.Add("Suppressed/acted Starts by position: " + string.Join("; ", byPos));

            var harmful = suppressedStarts.Count(o => (o.ActualDecisionDifferential ?? 0) < 0);
            var helpfulKeptAway = harmful; // suppressing a negative-value Start saves value
            var mistaken = suppressedStarts.Count(o => (o.ActualDecisionDifferential ?? 0) > 0);
            notes.Add(
                $"Among acted Starts: {helpfulKeptAway} had negative actual decision value " +
                $"(suppression helps); {mistaken} had positive value (suppression hurts).");

            var thin = suppressedStarts.Where(o => (o.DecisionValueMargin ?? o.RecommendationMargin ?? 0) < 3).ToList();
            notes.Add($"Thin-edge acted Starts (margin<3): n={thin.Count}.");
        }
        else
        {
            notes.Add("Policy did not suppress any Start decisions on holdout control set.");
        }

        var lowConfWrong = controlObs
            .Where(o => o.CalibratedConfidence <= policy.Threshold && o.WasCorrect == false)
            .ToList();
        notes.Add(
            $"Low-calibrated-confidence (≤{policy.Threshold}) incorrect control decisions: {lowConfWrong.Count}.");

        return notes;
    }

    private static void AssertProjectionV2Frozen()
    {
        if (FrozenProjectionCalibrationV2.Method != ProjectionCalibrationMethod.PiecewiseScaleAt20 ||
            Math.Abs(FrozenProjectionCalibrationV2.HighSlope - 0.6005) > 1e-9 ||
            Math.Abs(FrozenProjectionCalibrationV2.LowSlope - 0.9240) > 1e-9)
        {
            throw new InvalidOperationException("Projection V2 frozen parameters were modified.");
        }
    }

    private static void AssertConfidenceV2Frozen()
    {
        if (!FrozenDecisionConfidenceCalibrationV2.BinStarts.SequenceEqual(new[] { 0, 15, 25, 35 }) ||
            !FrozenDecisionConfidenceCalibrationV2.CalibratedRates.SequenceEqual(new[] { 57, 67, 65, 42 }))
        {
            throw new InvalidOperationException("Confidence V2 frozen mapping was modified.");
        }
    }

    private static void AssertFrozenMatchesSelected(DecisionPolicyCandidate selected)
    {
        if (!string.Equals(selected.Kind, FrozenConfidenceAwareDecisionPolicyV1.Kind, StringComparison.Ordinal) ||
            selected.Threshold != FrozenConfidenceAwareDecisionPolicyV1.MaxCalibratedConfidenceToSuppressStart ||
            Math.Abs(selected.Margin - FrozenConfidenceAwareDecisionPolicyV1.MaxDecisionValueMarginToSuppress) > 1e-9)
        {
            throw new InvalidOperationException(
                "Frozen decision-policy constants do not match development LOOCV selection. " +
                $"Selected {selected.CandidateId} but frozen is " +
                $"{FrozenConfidenceAwareDecisionPolicyV1.Kind}@t" +
                $"{FrozenConfidenceAwareDecisionPolicyV1.MaxCalibratedConfidenceToSuppressStart}-m" +
                $"{FrozenConfidenceAwareDecisionPolicyV1.MaxDecisionValueMarginToSuppress:0}.");
        }
    }

    private static (ProjectionExperimentVerdict Verdict, string Rationale) Judge(
        double devMeanDelta,
        DecisionPolicyScopeMetrics holdoutControl,
        DecisionPolicyScopeMetrics holdoutExperiment)
    {
        var holdDelta = (holdoutExperiment.TotalDecisionValue ?? 0) -
                        (holdoutControl.TotalDecisionValue ?? 0);
        var retention = holdoutControl.GradedDecisions == 0
            ? 1.0
            : (double)holdoutExperiment.GradedDecisions / holdoutControl.GradedDecisions;

        if (retention < ConfidenceAwareDecisionPolicySuccessCriteria.MinHoldoutDecisionRetention)
        {
            return (
                ProjectionExperimentVerdict.Regression,
                $"Holdout retention {retention:0%} < " +
                $"{ConfidenceAwareDecisionPolicySuccessCriteria.MinHoldoutDecisionRetention:0%} — abstention gaming.");
        }

        if (holdDelta <= -ConfidenceAwareDecisionPolicySuccessCriteria.MinHoldoutTotalValueImprovement)
        {
            return (
                ProjectionExperimentVerdict.Regression,
                $"Holdout total decision value worsened by {holdDelta:0.00} (control " +
                $"{holdoutControl.TotalDecisionValue:0.00} → experiment {holdoutExperiment.TotalDecisionValue:0.00}).");
        }

        var devOk = devMeanDelta >= ConfidenceAwareDecisionPolicySuccessCriteria.MinDevMeanTotalValueImprovement;
        var holdOk = holdDelta >= ConfidenceAwareDecisionPolicySuccessCriteria.MinHoldoutTotalValueImprovement;

        if (devOk && holdOk)
        {
            return (
                ProjectionExperimentVerdict.Improvement,
                $"Dev LOOCV mean Δ={devMeanDelta:0.00}; holdout Δ={holdDelta:0.00}; " +
                $"retention={retention:0%}. Accept confidence-aware policy.");
        }

        if (Math.Abs(holdDelta) < 5.0)
        {
            return (
                ProjectionExperimentVerdict.NoMaterialImprovement,
                $"Holdout Δ={holdDelta:0.00} is a tiny fluctuation (dev mean Δ={devMeanDelta:0.00}). " +
                "Do not accept the policy.");
        }

        if (holdOk && !devOk)
        {
            return (
                ProjectionExperimentVerdict.Inconclusive,
                $"Holdout improved (Δ={holdDelta:0.00}) but development LOOCV mean Δ={devMeanDelta:0.00} " +
                $"< {ConfidenceAwareDecisionPolicySuccessCriteria.MinDevMeanTotalValueImprovement}.");
        }

        return (
            ProjectionExperimentVerdict.NoMaterialImprovement,
            $"Criteria not met (dev mean Δ={devMeanDelta:0.00}, holdout Δ={holdDelta:0.00}, " +
            $"retention={retention:0%}). Keep existing decision rules.");
    }
}
