using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Players.Data;
using Playbook.Core.Decisions;
using Playbook.Core.Knowledge;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Knowledge;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class DataSufficiencyTrustExperimentTests
{
    [Fact]
    public void Experiment_Isolates_Trust_Gate_Without_Rejected_Transforms()
    {
        Assert.Equal(
            KnowledgeImpactGroup.DataSufficiencyTrust,
            FrozenDataSufficiencyTrustExperimentV1.ExperimentalGroups);
        Assert.False(FrozenDataSufficiencyTrustExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.Usage));
        Assert.False(FrozenDataSufficiencyTrustExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.RoleHealth));
        Assert.False(FrozenDataSufficiencyTrustExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.RecentForm));
        Assert.False(FrozenDataSufficiencyTrustExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.RecentFormThinMargin));
        Assert.Equal(new[] { 8, 12, 16 }, FrozenDataSufficiencyTrustExperimentV1.CandidateLimitedPenalties);
        Assert.Equal(8, FrozenDataSufficiencyTrustExperimentV1.InsufficientExtraPenalty);
        Assert.DoesNotContain(2024, FrozenDataSufficiencyTrustExperimentV1.DevelopmentSeasons);
    }

    [Fact]
    public void Frozen_Layers_Unchanged()
    {
        Assert.Equal(ProjectionCalibrationMethod.PiecewiseScaleAt20, FrozenProjectionCalibrationV2.Method);
        Assert.Equal(0.6005, FrozenProjectionCalibrationV2.HighSlope);
        Assert.Equal(0.9240, FrozenProjectionCalibrationV2.LowSlope);
        Assert.Equal(DecisionPolicyKinds.SuppressStartAndSit, FrozenConfidenceAwareDecisionPolicyV1.Kind);
        Assert.Equal(KnowledgeImpactGroup.Usage, FrozenKnowledgeImpactExperimentV1.FrozenEnhancedGroups);
    }

    [Fact]
    public void Trust_Gate_Penalizes_Limited_And_Insufficient_Confidence_Only()
    {
        var limited = SampleWithSufficiency(DataSufficiency.Limited, knowledgeConfidence: 60, opportunity: 80);
        var sufficient = SampleWithSufficiency(DataSufficiency.Sufficient, knowledgeConfidence: 60, opportunity: 80);
        var insufficient = SampleWithSufficiency(DataSufficiency.Insufficient, knowledgeConfidence: 60, opportunity: 80);

        var lim = KnowledgeImpactApplicator.ToEnhanced(
            limited, KnowledgeImpactGroup.DataSufficiencyTrust, null, 3.0, limitedPenalty: 12);
        var suf = KnowledgeImpactApplicator.ToEnhanced(
            sufficient, KnowledgeImpactGroup.DataSufficiencyTrust, null, 3.0, limitedPenalty: 12);
        var insuff = KnowledgeImpactApplicator.ToEnhanced(
            insufficient, KnowledgeImpactGroup.DataSufficiencyTrust, null, 3.0, limitedPenalty: 12);

        // Baseline strips opportunity; trust gate must NOT restore it.
        Assert.Null(lim.OpportunityScore);
        Assert.Null(suf.OpportunityScore);
        Assert.Null(insuff.OpportunityScore);

        // Baseline reduces confidence by 15 → 45, then Limited −12 → 33.
        Assert.Equal(45 - 12, lim.KnowledgeConfidence);
        Assert.Equal(45, suf.KnowledgeConfidence);
        Assert.Equal(45 - 20, insuff.KnowledgeConfidence);

        Assert.Contains(lim.Facts, f => f.Statement.Contains("DataSufficiencyTrust", StringComparison.Ordinal));
        Assert.DoesNotContain(lim.Signals, s => s.Type == SignalType.RecentProduction);
        Assert.DoesNotContain(lim.Signals, s => s.Type == SignalType.Usage);
    }

    [Fact]
    public void Sufficiency_Extracted_From_Cutoff_Safe_Fact()
    {
        var facts = new List<KnowledgeFact>
        {
            new()
            {
                Key = "projection.data_sufficiency",
                Statement = "Projection data sufficiency: Limited.",
                Source = "HistoricalFeatureReconstructor",
                Status = EvidenceStatus.Known
            }
        };
        Assert.Equal(DataSufficiency.Limited, KnowledgeImpactApplicator.ExtractDataSufficiency(facts));
        Assert.Equal(12, KnowledgeImpactApplicator.PenaltyFor(DataSufficiency.Limited, 12));
        Assert.Equal(20, KnowledgeImpactApplicator.PenaltyFor(DataSufficiency.Insufficient, 12));
        Assert.Equal(0, KnowledgeImpactApplicator.PenaltyFor(DataSufficiency.Sufficient, 12));
    }

    [Fact]
    public void Controlled_Fixture_Carries_DataSufficiency_Into_Snapshot()
    {
        var (snapshot, _) = new HistoricalSnapshotBuilder().Build(ControlledHistoricalFixture.Create());
        Assert.Equal(
            DataSufficiency.Sufficient,
            snapshot.Players.Single(p => p.PlayerId == ControlledHistoricalFixture.AlphaRbId).DataSufficiency);
        Assert.Equal(
            DataSufficiency.Limited,
            snapshot.Players.Single(p => p.PlayerId == ControlledHistoricalFixture.BravoRbId).DataSufficiency);
    }

    [Fact]
    public async Task Official_DataSufficiencyTrust_Experiment_Runs_Once()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunDataSufficiencyTrustExperimentAsync(provider);

        Assert.Equal(KnowledgeImpactGroup.DataSufficiencyTrust, report.FrozenGroups);
        Assert.False(report.UsedHoldoutDuringFitting);
        Assert.True(report.ProjectionV2Unchanged);
        Assert.True(report.ConfidenceV2Unchanged);
        Assert.True(report.DecisionPolicyV1Unchanged);
        Assert.Equal(2024, report.HoldoutSeason);
        Assert.Contains(FrozenDataSufficiencyTrustExperimentV1.SelectedLimitedPenalty,
            FrozenDataSufficiencyTrustExperimentV1.CandidateLimitedPenalties);
        Assert.DoesNotContain(report.AblationRows, r => r.Group.HasFlag(KnowledgeImpactGroup.Usage));
        Assert.DoesNotContain(report.AblationRows, r => r.Group.HasFlag(KnowledgeImpactGroup.RecentForm));
        Assert.Contains("Quick Picks", report.QuickPicksEvaluationNote);

        Assert.True(
            report.Verdict is ProjectionExperimentVerdict.Improvement
                or ProjectionExperimentVerdict.NoMaterialImprovement
                or ProjectionExperimentVerdict.Regression
                or ProjectionExperimentVerdict.Inconclusive);

        var state = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        Assert.Equal(KnowledgeMode.Passthrough, state.Mode);

        var text = report.ToReportText();
        Assert.Contains("DataSufficiencyTrust", text);
        Assert.Contains("HOLDOUT", text, StringComparison.OrdinalIgnoreCase);

        var outPath = Path.Combine(AppContext.BaseDirectory, "DATA_SUFFICIENCY_TRUST_EXPERIMENT_V1_REPORT.txt");
        await File.WriteAllTextAsync(outPath, text);
        Assert.True(File.Exists(outPath));
    }

    private static PlayerKnowledge SampleWithSufficiency(
        DataSufficiency sufficiency,
        int knowledgeConfidence,
        int? opportunity)
    {
        return new PlayerKnowledge
        {
            PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PlayerName = "Sample",
            PositionLabel = "RB",
            Facts =
            [
                new KnowledgeFact
                {
                    Key = "projection.data_sufficiency",
                    Statement = $"Projection data sufficiency: {sufficiency}.",
                    Source = "HistoricalFeatureReconstructor",
                    Status = EvidenceStatus.Known
                },
                new KnowledgeFact
                {
                    Key = "production.recent",
                    Statement = "Recent production 70",
                    Source = "test",
                    Status = EvidenceStatus.Known
                }
            ],
            Signals =
            [
                new KnowledgeSignal
                {
                    Type = SignalType.Projection,
                    Value = 14,
                    Direction = SignalDirection.Positive,
                    Strength = SignalStrength.Moderate,
                    Confidence = 60,
                    Status = EvidenceStatus.Known,
                    Source = "test",
                    Explanation = "Projection 14"
                },
                new KnowledgeSignal
                {
                    Type = SignalType.RecentProduction,
                    Value = 70,
                    Direction = SignalDirection.Positive,
                    Strength = SignalStrength.Moderate,
                    Confidence = 60,
                    Status = EvidenceStatus.Known,
                    Source = "test",
                    Explanation = "Recent production 70"
                },
                new KnowledgeSignal
                {
                    Type = SignalType.Opportunity,
                    Value = opportunity,
                    Direction = SignalDirection.Positive,
                    Strength = SignalStrength.Moderate,
                    Confidence = 60,
                    Status = EvidenceStatus.Known,
                    Source = "test",
                    Explanation = $"Opportunity {opportunity}"
                }
            ],
            OverallStatus = EvidenceStatus.Known,
            KnowledgeConfidence = knowledgeConfidence,
            MissingEvidence = [],
            GeneratedAt = DateTimeOffset.UtcNow,
            ProjectedPoints = 14m,
            ProjectionConfidence = 60,
            OpportunityScore = opportunity,
            UsageScore = 70,
            HealthLabel = "Healthy"
        };
    }
}
