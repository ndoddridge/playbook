using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Players.Data;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;
using Playbook.Infrastructure.Replay.Calibration;

namespace Playbook.Tests;

public class ConfidenceCalibrationExperimentTests
{
    [Fact]
    public void Fitter_Rejects_Holdout_Observations()
    {
        var obs = new List<ConfidenceCalibrationObservation>
        {
            Obs(2015, 12, true),
            Obs(2018, 30, false),
            Obs(2024, 30, true)
        };

        Assert.Throws<InvalidOperationException>(() =>
            ConfidenceCalibrationFitter.SelectAndFreeze(
                obs,
                FrozenDecisionConfidenceCalibrationV2.DevelopmentSeasons,
                FrozenDecisionConfidenceCalibrationV2.HoldoutSeason));
    }

    [Fact]
    public void Calibrated_Apply_Is_Deterministic()
    {
        var a = FrozenDecisionConfidenceCalibrationV2.Apply(12);
        var b = FrozenDecisionConfidenceCalibrationV2.Apply(12);
        Assert.Equal(a, b);
        Assert.InRange(a, 1, 99);
    }

    [Fact]
    public void Projection_V2_Frozen_Parameters_Unchanged()
    {
        Assert.Equal(ProjectionCalibrationMethod.PiecewiseScaleAt20, FrozenProjectionCalibrationV2.Method);
        Assert.Equal(0.6005, FrozenProjectionCalibrationV2.HighSlope);
        Assert.Equal(0.9240, FrozenProjectionCalibrationV2.LowSlope);
    }

    [Fact]
    public async Task Default_Mode_Keeps_Frozen_2018_Benchmark()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<HistoricalProjectionExperimentState>();
        Assert.Equal(HistoricalProjectionPrimaryMode.ProjectionV1, state.PrimaryMode);

        var scorecard = await HistoricalReplayCommands.RunReal2018SeasonAsync(provider);
        Assert.Equal(Frozen2018SeasonBenchmark.CurrentModelMae, scorecard.CurrentModelMae);
        Assert.Equal(Frozen2018SeasonBenchmark.DecisionAccuracyPercent, scorecard.DecisionAccuracyPercent);
    }

    [Fact]
    public async Task Official_Confidence_Calibration_Experiment_Runs_Once()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunConfidenceCalibrationExperimentAsync(provider);

        Assert.False(report.UsedHoldoutDuringFitting);
        Assert.True(report.ProjectionV2Unchanged);
        Assert.Equal(2024, report.HoldoutSeason);
        Assert.Equal(FrozenDecisionConfidenceCalibrationV2.DevelopmentSeasons, report.DevelopmentSeasons);
        Assert.NotNull(report.DevelopmentRaw.Ece);
        Assert.NotNull(report.DevelopmentCalibrated.Ece);
        Assert.NotNull(report.HoldoutRaw.Ece);
        Assert.NotNull(report.HoldoutCalibrated.Ece);
        Assert.Equal(0, report.RecommendationsAffectedByConfidenceThresholds);
        Assert.True(report.RecommendationsUnchangedOnHoldout);

        // Default projection mode restored.
        var state = provider.GetRequiredService<HistoricalProjectionExperimentState>();
        Assert.Equal(HistoricalProjectionPrimaryMode.ProjectionV1, state.PrimaryMode);

        var text = report.ToReportText();
        Assert.Contains("CONFIDENCE CALIBRATION EXPERIMENT", text);
        Assert.Contains("OFFICIAL HOLDOUT 2024", text);
        Assert.Contains("VERDICT", text);

        var outPath = Path.Combine(AppContext.BaseDirectory, "CONFIDENCE_CALIBRATION_V2_REPORT.txt");
        await File.WriteAllTextAsync(outPath, text);
        Assert.True(File.Exists(outPath));
    }

    private static ConfidenceCalibrationObservation Obs(int season, int raw, bool correct) =>
        new()
        {
            Season = season,
            Week = 7,
            DecisionId = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            PlayerName = "P",
            RawConfidence = raw,
            WasCorrect = correct,
            DecisionDifferential = correct ? 3 : -3
        };
}
