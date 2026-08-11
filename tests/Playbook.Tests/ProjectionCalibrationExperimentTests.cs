using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Players.Data;
using Playbook.Core.Players;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;
using Playbook.Infrastructure.Replay.Calibration;
using Playbook.Infrastructure.Replay.Reconstruction;

namespace Playbook.Tests;

public class ProjectionCalibrationExperimentTests
{
    [Fact]
    public void Fitter_Rejects_Holdout_Season_Observations()
    {
        var obs = new List<CalibrationObservation>
        {
            Obs(2015, 10, 12),
            Obs(2018, 14, 11),
            Obs(2024, 20, 10) // forbidden
        };

        Assert.Throws<InvalidOperationException>(() =>
            ProjectionCalibrationFitter.SelectAndFreeze(
                obs,
                FrozenProjectionCalibrationV2.DevelopmentSeasons,
                FrozenProjectionCalibrationV2.HoldoutSeason));
    }

    [Fact]
    public void V2_Apply_Is_Deterministic_And_Shrinks_High_Projections()
    {
        var a = FrozenProjectionCalibrationV2.Apply(30);
        var b = FrozenProjectionCalibrationV2.Apply(30);
        Assert.Equal(a, b);
        Assert.True(a < 30);
        Assert.Equal(
            Math.Round(30 * FrozenProjectionCalibrationV2.HighSlope, 1, MidpointRounding.AwayFromZero),
            a);

        var low = FrozenProjectionCalibrationV2.Apply(15);
        Assert.Equal(
            Math.Round(15 * FrozenProjectionCalibrationV2.LowSlope, 1, MidpointRounding.AwayFromZero),
            low);
    }

    [Fact]
    public void V1_Engine_Formulas_Unchanged_ModelId()
    {
        Assert.Equal("baseline-opportunity-aware-v1", OpportunityAwareProjectionEngine.Id);
        var v1 = new OpportunityAwareProjectionEngine();
        var v2 = new CalibratedOpportunityAwareProjectionEngine(v1);
        Assert.Equal(FrozenProjectionCalibrationV2.ModelId, v2.ModelId);
        Assert.NotEqual(v1.ModelId, v2.ModelId);
    }

    [Fact]
    public async Task Default_Mode_Keeps_Frozen_2018_Benchmark()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<HistoricalProjectionExperimentState>();
        Assert.Equal(HistoricalProjectionPrimaryMode.ProjectionV1, state.PrimaryMode);

        var scorecard = await HistoricalReplayCommands.RunReal2018SeasonAsync(provider);
        Assert.Equal(Frozen2018SeasonBenchmark.CurrentModelMae, scorecard.CurrentModelMae);
        Assert.Equal(Frozen2018SeasonBenchmark.BaselineAMae, scorecard.BaselineAMae);
        Assert.Equal(Frozen2018SeasonBenchmark.DecisionAccuracyPercent, scorecard.DecisionAccuracyPercent);
        Assert.Equal(Frozen2018SeasonBenchmark.TotalDecisionValue, scorecard.TotalDecisionValue);
    }

    [Fact]
    public async Task Official_Projection_Calibration_Experiment_Runs_Once()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunProjectionCalibrationExperimentAsync(provider);

        Assert.False(report.UsedHoldoutDuringFitting);
        Assert.Equal(FrozenProjectionCalibrationV2.DevelopmentSeasons, report.DevelopmentSeasons);
        Assert.Equal(2024, report.HoldoutSeason);
        Assert.Equal(FrozenProjectionCalibrationV2.Method, report.SelectedMethod);

        Assert.True(report.DevV2.Mae < report.DevV1.Mae);
        Assert.True(Math.Abs(report.DevV2.Bias!.Value) < Math.Abs(report.DevV1.Bias!.Value));

        Assert.NotNull(report.HoldoutV1.Mae);
        Assert.NotNull(report.HoldoutV2.Mae);
        Assert.NotNull(report.HoldoutBaselineA.Mae);
        Assert.True(report.HoldoutDecisionImpact.TotalDecisionsV1 > 0);
        Assert.True(report.HoldoutDecisionImpact.TotalDecisionsV2 > 0);

        // Default mode restored after experiment.
        var state = provider.GetRequiredService<HistoricalProjectionExperimentState>();
        Assert.Equal(HistoricalProjectionPrimaryMode.ProjectionV1, state.PrimaryMode);

        var text = report.ToReportText();
        Assert.Contains("PROJECTION CALIBRATION EXPERIMENT", text);
        Assert.Contains("OFFICIAL HOLDOUT 2024", text);
        Assert.Contains("VERDICT", text);

        var outPath = Path.Combine(AppContext.BaseDirectory, "PROJECTION_CALIBRATION_V2_REPORT.txt");
        await File.WriteAllTextAsync(outPath, text);
        Assert.True(File.Exists(outPath));
    }

    private static CalibrationObservation Obs(int season, double v1, double actual) =>
        new()
        {
            Season = season,
            Week = 7,
            PlayerId = Guid.NewGuid(),
            PlayerName = "P",
            Position = Position.WR,
            V1Predicted = v1,
            Actual = actual,
            BaselineAPredicted = actual
        };
}
