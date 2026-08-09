using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Players.Data;
using Playbook.Application.Replay;
using Playbook.Core.Decisions;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;
using Playbook.Infrastructure.Replay.Calibration;

namespace Playbook.Tests;

public class ConfidenceAwareDecisionPolicyTests
{
    [Fact]
    public void Fitter_Rejects_Holdout_Observations()
    {
        var obs = new List<DecisionPolicyObservation>
        {
            Obs(2015, DecisionRecommendation.Start, 42, margin: 2, differential: -5),
            Obs(2018, DecisionRecommendation.Start, 42, margin: 2, differential: -3),
            Obs(2024, DecisionRecommendation.Start, 42, margin: 2, differential: -4)
        };

        Assert.Throws<InvalidOperationException>(() =>
            ConfidenceAwareDecisionPolicyFitter.SelectViaLeaveOneSeasonOut(
                obs,
                FrozenConfidenceAwareDecisionPolicyV1.DevelopmentSeasons,
                FrozenConfidenceAwareDecisionPolicyV1.HoldoutSeason));
    }

    [Fact]
    public void Policy_Suppresses_Low_Confidence_Marginal_Start()
    {
        var definition = new ConfidenceAwareDecisionPolicyDefinition
        {
            CandidateId = "test",
            Kind = DecisionPolicyKinds.SuppressStart,
            Threshold = 45,
            Margin = 6,
            HighTrustMin = 60,
            Description = "test"
        };

        var recs = new List<StartSitRecommendation>
        {
            Rec(StartSitAction.Start, "A", cal: 42, margin: 2),
            Rec(StartSitAction.Sit, "B", cal: 42, margin: 2)
        };

        var result = ConfidenceAwareDecisionPolicyApplicator.ApplyDefinition(recs, definition);
        Assert.Equal(1, result.SuppressedCount);
        Assert.DoesNotContain(result.Recommendations, r => r.Action == StartSitAction.Start && r.PlayerName == "A");
        Assert.Contains(result.Recommendations, r => r.Action == StartSitAction.Sit && r.PlayerName == "B");
    }

    [Fact]
    public void Policy_Keeps_High_Confidence_Start()
    {
        var definition = ConfidenceAwareDecisionPolicyDefinition.FromFrozen();
        var recs = new List<StartSitRecommendation>
        {
            Rec(StartSitAction.Start, "Ace", cal: 67, margin: 1),
            Rec(StartSitAction.Sit, "Bench", cal: 67, margin: 1)
        };

        var result = ConfidenceAwareDecisionPolicyApplicator.ApplyDefinition(recs, definition);
        Assert.Equal(0, result.SuppressedCount);
        Assert.Contains(result.Recommendations, r => r.Action == StartSitAction.Start && r.PlayerName == "Ace");
        Assert.Equal(DecisionTrustLabel.HighTrust,
            result.Recommendations.First(r => r.PlayerName == "Ace").TrustLabel);
    }

    [Fact]
    public void Policy_Keeps_Low_Confidence_Start_With_Strong_Margin()
    {
        var definition = ConfidenceAwareDecisionPolicyDefinition.FromFrozen();
        var recs = new List<StartSitRecommendation>
        {
            Rec(StartSitAction.Start, "Stud", cal: 42, margin: 10)
        };

        var result = ConfidenceAwareDecisionPolicyApplicator.ApplyDefinition(recs, definition);
        Assert.Equal(0, result.SuppressedCount);
        Assert.Contains(result.Recommendations, r => r.Action == StartSitAction.Start);
        Assert.Equal(DecisionTrustLabel.LowTrust,
            result.Recommendations.First().TrustLabel);
    }

    [Fact]
    public void Policy_Boundary_Threshold_Inclusive()
    {
        var definition = new ConfidenceAwareDecisionPolicyDefinition
        {
            CandidateId = "bound",
            Kind = DecisionPolicyKinds.SuppressStart,
            Threshold = 45,
            Margin = 6,
            HighTrustMin = 60,
            Description = "bound"
        };

        var at = ConfidenceAwareDecisionPolicyApplicator.ApplyDefinition(
            [Rec(StartSitAction.Start, "At", cal: 45, margin: 2)],
            definition);
        var above = ConfidenceAwareDecisionPolicyApplicator.ApplyDefinition(
            [Rec(StartSitAction.Start, "Above", cal: 46, margin: 2)],
            definition);

        Assert.Equal(1, at.SuppressedCount);
        Assert.Equal(0, above.SuppressedCount);
    }

    [Fact]
    public void Policy_Application_Is_Deterministic()
    {
        var definition = ConfidenceAwareDecisionPolicyDefinition.FromFrozen();
        var recs = new List<StartSitRecommendation>
        {
            Rec(StartSitAction.Start, "A", cal: 42, margin: 2),
            Rec(StartSitAction.Sit, "B", cal: 65, margin: 2)
        };

        var a = ConfidenceAwareDecisionPolicyApplicator.ApplyDefinition(recs, definition);
        var b = ConfidenceAwareDecisionPolicyApplicator.ApplyDefinition(recs, definition);
        Assert.Equal(a.SuppressedCount, b.SuppressedCount);
        Assert.Equal(
            a.Recommendations.Select(r => (r.PlayerId, r.Action, r.TrustLabel)).ToArray(),
            b.Recommendations.Select(r => (r.PlayerId, r.Action, r.TrustLabel)).ToArray());
    }

    [Fact]
    public void Control_Mode_Does_Not_Alter_Recommendations()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<ConfidenceAwareDecisionPolicyState>();
        var policy = provider.GetRequiredService<IConfidenceAwareDecisionPolicy>();
        Assert.Equal(ConfidenceAwareDecisionPolicyMode.Off, state.Mode);

        var recs = new List<StartSitRecommendation>
        {
            Rec(StartSitAction.Start, "A", cal: 42, margin: 1)
        };
        var result = policy.Apply(recs);
        Assert.Equal(0, result.SuppressedCount);
        Assert.Equal(DecisionTrustLabel.Unspecified, result.Recommendations[0].TrustLabel);
        Assert.Single(result.Recommendations);
    }

    [Fact]
    public void Experimental_Mode_Applies_Frozen_Policy()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<ConfidenceAwareDecisionPolicyState>();
        var policy = provider.GetRequiredService<IConfidenceAwareDecisionPolicy>();
        state.Mode = ConfidenceAwareDecisionPolicyMode.On;

        var recs = new List<StartSitRecommendation>
        {
            Rec(StartSitAction.Start, "A", cal: 42, margin: 1),
            Rec(StartSitAction.Sit, "B", cal: 42, margin: 1)
        };
        var result = policy.Apply(recs);
        Assert.True(result.SuppressedCount >= 1);
        Assert.Empty(result.Recommendations);
        state.Mode = ConfidenceAwareDecisionPolicyMode.Off;
    }

    [Fact]
    public void Projection_And_Confidence_V2_Unchanged()
    {
        Assert.Equal(ProjectionCalibrationMethod.PiecewiseScaleAt20, FrozenProjectionCalibrationV2.Method);
        Assert.Equal(0.6005, FrozenProjectionCalibrationV2.HighSlope);
        Assert.Equal(0.9240, FrozenProjectionCalibrationV2.LowSlope);
        Assert.Equal(new[] { 0, 15, 25, 35 }, FrozenDecisionConfidenceCalibrationV2.BinStarts);
        Assert.Equal(new[] { 57, 67, 65, 42 }, FrozenDecisionConfidenceCalibrationV2.CalibratedRates);
    }

    [Fact]
    public async Task Default_Modes_Keep_Frozen_2018_Benchmark()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var projection = provider.GetRequiredService<HistoricalProjectionExperimentState>();
        var policy = provider.GetRequiredService<ConfidenceAwareDecisionPolicyState>();
        Assert.Equal(HistoricalProjectionPrimaryMode.ProjectionV1, projection.PrimaryMode);
        Assert.Equal(ConfidenceAwareDecisionPolicyMode.Off, policy.Mode);

        var scorecard = await HistoricalReplayCommands.RunReal2018SeasonAsync(provider);
        Assert.Equal(Frozen2018SeasonBenchmark.CurrentModelMae, scorecard.CurrentModelMae);
        Assert.Equal(Frozen2018SeasonBenchmark.DecisionAccuracyPercent, scorecard.DecisionAccuracyPercent);
    }

    [Fact]
    public async Task Official_Decision_Policy_Experiment_Runs_Once()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunConfidenceAwareDecisionPolicyExperimentAsync(provider);

        Assert.False(report.UsedHoldoutDuringFitting);
        Assert.True(report.ProjectionV2Unchanged);
        Assert.True(report.ConfidenceV2Unchanged);
        Assert.Equal(2024, report.HoldoutSeason);
        Assert.Equal(FrozenConfidenceAwareDecisionPolicyV1.DevelopmentSeasons, report.DevelopmentSeasons);
        Assert.Equal(
            $"{FrozenConfidenceAwareDecisionPolicyV1.Kind}@t" +
            $"{FrozenConfidenceAwareDecisionPolicyV1.MaxCalibratedConfidenceToSuppressStart}-m" +
            $"{FrozenConfidenceAwareDecisionPolicyV1.MaxDecisionValueMarginToSuppress:0}",
            report.SelectedCandidateId);

        var projection = provider.GetRequiredService<HistoricalProjectionExperimentState>();
        var policy = provider.GetRequiredService<ConfidenceAwareDecisionPolicyState>();
        Assert.Equal(HistoricalProjectionPrimaryMode.ProjectionV1, projection.PrimaryMode);
        Assert.Equal(ConfidenceAwareDecisionPolicyMode.Off, policy.Mode);

        var text = report.ToReportText();
        Assert.Contains("CONFIDENCE-AWARE DECISION POLICY EXPERIMENT", text);
        Assert.Contains("OFFICIAL HOLDOUT 2024", text);
        Assert.Contains("VERDICT", text);

        var outPath = Path.Combine(AppContext.BaseDirectory, "CONFIDENCE_AWARE_DECISION_POLICY_V1_REPORT.txt");
        await File.WriteAllTextAsync(outPath, text);
        Assert.True(File.Exists(outPath));
    }

    [Fact]
    public void Offline_Swap_Flips_Decision_Value_Sign()
    {
        var obs = new List<DecisionPolicyObservation>
        {
            Obs(2015, DecisionRecommendation.Start, 42, margin: 1, differential: -8)
        };
        var candidate = new DecisionPolicyCandidate
        {
            CandidateId = "SwapStart@t45-m6",
            Kind = DecisionPolicyKinds.SwapStart,
            Threshold = 45,
            Margin = 6,
            Description = "swap"
        };

        var metrics = ConfidenceAwareDecisionPolicyFitter.Evaluate("swap", obs, candidate);
        Assert.Equal(1, metrics.SwappedStarts);
        Assert.Equal(8.0, metrics.TotalDecisionValue);
    }

    private static DecisionPolicyObservation Obs(
        int season,
        DecisionRecommendation recommendation,
        int calibrated,
        double margin,
        double differential) =>
        new()
        {
            Season = season,
            Week = 7,
            DecisionId = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            PlayerName = "P",
            Position = Position.WR,
            Recommendation = recommendation,
            RawConfidence = 30,
            CalibratedConfidence = calibrated,
            DecisionValue = 10,
            DecisionValueMargin = margin,
            RecommendationMargin = margin,
            ActualDecisionDifferential = differential,
            WasCorrect = differential >= 0
        };

    private static StartSitRecommendation Rec(
        StartSitAction action,
        string name,
        int cal,
        double margin) =>
        new()
        {
            Action = action,
            PlayerId = Guid.NewGuid(),
            PlayerName = name,
            PositionLabel = "WR",
            ProjectionSummary = "10.0 pts",
            Confidence = 30,
            CalibratedConfidence = cal,
            DecisionValue = 12,
            DecisionValueMargin = margin,
            Reasons = ["signal"],
            InsufficientData = false
        };
}
