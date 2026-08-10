using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Players.Data;
using Playbook.Core.Decisions;
using Playbook.Core.Knowledge;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Knowledge;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class RecentFormThinMarginExperimentTests
{
    [Fact]
    public void Experiment_Isolates_ThinMargin_RecentForm_Only()
    {
        Assert.Equal(
            KnowledgeImpactGroup.RecentFormThinMargin,
            FrozenRecentFormThinMarginExperimentV1.ExperimentalGroups);
        Assert.False(FrozenRecentFormThinMarginExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.Usage));
        Assert.False(FrozenRecentFormThinMarginExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.RoleHealth));
        Assert.Equal(3.0, FrozenRecentFormThinMarginExperimentV1.ThinMarginMaxPoints);
        Assert.Equal(65, FrozenRecentFormThinMarginExperimentV1.HighThreshold);
        Assert.Equal(35, FrozenRecentFormThinMarginExperimentV1.LowThreshold);
        Assert.Equal(6, FrozenRecentFormThinMarginExperimentV1.StartSitOpportunityDelta);
        Assert.DoesNotContain(2024, FrozenRecentFormThinMarginExperimentV1.DevelopmentSeasons);
    }

    [Fact]
    public void Frozen_Layers_Unchanged()
    {
        Assert.Equal(ProjectionCalibrationMethod.PiecewiseScaleAt20, FrozenProjectionCalibrationV2.Method);
        Assert.Equal(0.6005, FrozenProjectionCalibrationV2.HighSlope);
        Assert.Equal(0.9240, FrozenProjectionCalibrationV2.LowSlope);
        Assert.Equal(new[] { 0, 15, 25, 35 }, FrozenDecisionConfidenceCalibrationV2.BinStarts);
        Assert.Equal(new[] { 57, 67, 65, 42 }, FrozenDecisionConfidenceCalibrationV2.CalibratedRates);
        Assert.Equal(DecisionPolicyKinds.SuppressStartAndSit, FrozenConfidenceAwareDecisionPolicyV1.Kind);
        Assert.Equal(KnowledgeImpactGroup.Usage, FrozenKnowledgeImpactExperimentV1.FrozenEnhancedGroups);
    }

    [Fact]
    public void Thin_Margin_Gate_Applies_Only_When_Margin_Below_Threshold()
    {
        var highForm = SampleKnowledge(opportunity: 50, recentProd: 80, projected: 14m);
        var open = KnowledgeImpactApplicator.ToEnhanced(
            highForm,
            KnowledgeImpactGroup.RecentFormThinMargin,
            comparativeMargin: 2.0,
            thinMarginMax: 3.0);
        var closed = KnowledgeImpactApplicator.ToEnhanced(
            highForm,
            KnowledgeImpactGroup.RecentFormThinMargin,
            comparativeMargin: 5.0,
            thinMarginMax: 3.0);
        var ungated = KnowledgeImpactApplicator.ToEnhanced(
            highForm,
            KnowledgeImpactGroup.RecentForm);

        Assert.Equal(50 + 6, open.OpportunityScore);
        Assert.Null(closed.OpportunityScore); // baseline-stripped; gate closed → no delta from null/50 path
        // Closed starts from baseline (opportunity null). Gate closed → stays null (no delta applied).
        Assert.Equal(50 + 6, ungated.OpportunityScore);
        Assert.Contains(open.Facts, f => f.Statement.Contains("RecentFormThinMargin+", StringComparison.Ordinal));
        Assert.Contains(closed.Facts, f => f.Statement.Contains("gate closed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nearest_Projection_Margins_Are_Position_Local()
    {
        var a = SampleKnowledge(opportunity: 50, recentProd: 70, projected: 15m, position: "RB", id: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = SampleKnowledge(opportunity: 50, recentProd: 70, projected: 13m, position: "RB", id: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var c = SampleKnowledge(opportunity: 50, recentProd: 70, projected: 20m, position: "WR", id: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

        var margins = KnowledgeImpactApplicator.ComputeNearestProjectionMargins([a, b, c]);
        Assert.Equal(2.0, margins[a.PlayerId]);
        Assert.Equal(2.0, margins[b.PlayerId]);
        Assert.Null(margins[c.PlayerId]); // sole WR
    }

    [Fact]
    public void Rejected_Transforms_Not_Bundled()
    {
        Assert.False(FrozenRecentFormThinMarginExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.Usage));
        Assert.False(FrozenRecentFormThinMarginExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.RoleHealth));
        Assert.False(FrozenRecentFormThinMarginExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.RecentForm));
    }

    [Fact]
    public async Task Official_ThinMargin_Experiment_Runs_Once()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunRecentFormThinMarginExperimentAsync(provider);

        Assert.Equal(FrozenRecentFormThinMarginExperimentV1.ExperimentalGroups, report.FrozenGroups);
        Assert.False(report.UsedHoldoutDuringFitting);
        Assert.True(report.ProjectionV2Unchanged);
        Assert.True(report.ConfidenceV2Unchanged);
        Assert.True(report.DecisionPolicyV1Unchanged);
        Assert.Equal(2024, report.HoldoutSeason);
        Assert.Equal(new[] { 2015, 2018, 2021 }, report.DevelopmentSeasons);
        Assert.DoesNotContain(report.AblationRows, r => r.Group.HasFlag(KnowledgeImpactGroup.Usage));
        Assert.DoesNotContain(report.AblationRows, r => r.Group.HasFlag(KnowledgeImpactGroup.RoleHealth));

        Assert.True(
            report.Verdict is ProjectionExperimentVerdict.Improvement
                or ProjectionExperimentVerdict.NoMaterialImprovement
                or ProjectionExperimentVerdict.Regression
                or ProjectionExperimentVerdict.Inconclusive);

        var state = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        Assert.Equal(KnowledgeMode.Passthrough, state.Mode);

        var text = report.ToReportText();
        Assert.Contains("HOLDOUT", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RecentFormThinMargin", text);

        var outPath = Path.Combine(AppContext.BaseDirectory, "RECENT_FORM_THIN_MARGIN_EXPERIMENT_V1_REPORT.txt");
        await File.WriteAllTextAsync(outPath, text);
        Assert.True(File.Exists(outPath));
    }

    private static PlayerKnowledge SampleKnowledge(
        int? opportunity,
        int recentProd,
        decimal projected,
        string position = "RB",
        Guid? id = null)
    {
        return new PlayerKnowledge
        {
            PlayerId = id ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PlayerName = "Sample",
            PositionLabel = position,
            Facts =
            [
                new KnowledgeFact
                {
                    Key = "production.recent",
                    Statement = $"Recent production {recentProd}",
                    Source = "test",
                    Status = EvidenceStatus.Known
                }
            ],
            Signals =
            [
                new KnowledgeSignal
                {
                    Type = SignalType.Projection,
                    Value = (double)projected,
                    Direction = SignalDirection.Positive,
                    Strength = SignalStrength.Moderate,
                    Confidence = 60,
                    Status = EvidenceStatus.Known,
                    Source = "test",
                    Explanation = $"Projection {projected}"
                },
                new KnowledgeSignal
                {
                    Type = SignalType.RecentProduction,
                    Value = recentProd,
                    Direction = recentProd >= 65 ? SignalDirection.Positive : SignalDirection.Negative,
                    Strength = SignalStrength.Moderate,
                    Confidence = 60,
                    Status = EvidenceStatus.Known,
                    Source = "test",
                    Explanation = $"Recent production {recentProd}"
                }
            ],
            OverallStatus = EvidenceStatus.Known,
            KnowledgeConfidence = 60,
            MissingEvidence = [],
            GeneratedAt = DateTimeOffset.UtcNow,
            ProjectedPoints = projected,
            ProjectionConfidence = 60,
            OpportunityScore = opportunity,
            UsageScore = 50,
            HealthLabel = "Healthy"
        };
    }
}
