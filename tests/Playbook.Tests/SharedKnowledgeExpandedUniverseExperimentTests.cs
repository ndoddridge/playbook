using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Players.Data;
using Playbook.Core.Knowledge;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Knowledge;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class SharedKnowledgeExpandedUniverseExperimentTests
{
    [Fact]
    public void Protocol_Uses_Expanded_Universe_And_Keeps_Rejected_Transforms_Off()
    {
        Assert.Equal(
            HistoricalCandidateUniverse.ExpandedSkillUniverse,
            FrozenSharedKnowledgeExpandedUniverseExperimentV1.CandidateUniverse);
        Assert.Equal(KnowledgeMode.Baseline, FrozenSharedKnowledgeExpandedUniverseExperimentV1.ControlMode);
        Assert.Equal(KnowledgeMode.Passthrough, FrozenSharedKnowledgeExpandedUniverseExperimentV1.TreatmentMode);
        Assert.Equal(KnowledgeImpactGroup.None, FrozenSharedKnowledgeExpandedUniverseExperimentV1.ActiveGroups);
        Assert.False(FrozenSharedKnowledgeExpandedUniverseExperimentV1.ActiveGroups.HasFlag(KnowledgeImpactGroup.Usage));
        Assert.False(FrozenSharedKnowledgeExpandedUniverseExperimentV1.ActiveGroups.HasFlag(KnowledgeImpactGroup.RoleHealth));
        Assert.False(FrozenSharedKnowledgeExpandedUniverseExperimentV1.ActiveGroups.HasFlag(KnowledgeImpactGroup.RecentForm));
        Assert.False(FrozenSharedKnowledgeExpandedUniverseExperimentV1.ActiveGroups.HasFlag(KnowledgeImpactGroup.RecentFormThinMargin));
        Assert.False(FrozenSharedKnowledgeExpandedUniverseExperimentV1.ActiveGroups.HasFlag(KnowledgeImpactGroup.DataSufficiencyTrust));
        Assert.Equal(new[] { 2015, 2018, 2021 }, FrozenSharedKnowledgeExpandedUniverseExperimentV1.DevelopmentSeasons);
        Assert.Equal(2024, FrozenSharedKnowledgeExpandedUniverseExperimentV1.HoldoutSeason);
        Assert.DoesNotContain(2024, FrozenSharedKnowledgeExpandedUniverseExperimentV1.DevelopmentSeasons);
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
    public async Task Default_LabRoster_Path_Still_Matches_Frozen_2018_Benchmark()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var scorecard = await HistoricalReplayCommands.RunReal2018SeasonAsync(provider);
        Assert.Equal(Frozen2018SeasonBenchmark.CurrentModelMae, scorecard.CurrentModelMae);
        Assert.Equal(Frozen2018SeasonBenchmark.DecisionAccuracyPercent, scorecard.DecisionAccuracyPercent);
        Assert.Equal(Frozen2018SeasonBenchmark.TotalDecisionValue, scorecard.TotalDecisionValue);
    }

    [Fact]
    public async Task Expanded_Baseline_Vs_Passthrough_Is_Cutoff_Safe_And_Deterministic_On_Probe_Week()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var knowledgeState = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        var seasonRunner = provider.GetRequiredService<Application.Replay.IMultiWeekHistoricalReplayRunner>();

        knowledgeState.ConfigureBaseline();
        var a1 = await seasonRunner.RunAsync(new MultiWeekReplayRequest
        {
            Season = 2018,
            StartWeek = 7,
            EndWeek = 7,
            FixtureId = "nflverse",
            CandidateUniverse = HistoricalCandidateUniverse.ExpandedSkillUniverse
        });
        var a2 = await seasonRunner.RunAsync(new MultiWeekReplayRequest
        {
            Season = 2018,
            StartWeek = 7,
            EndWeek = 7,
            FixtureId = "nflverse",
            CandidateUniverse = HistoricalCandidateUniverse.ExpandedSkillUniverse
        });

        Assert.Equal(a1.TotalDecisions, a2.TotalDecisions);
        Assert.Equal(a1.TotalDecisionValue, a2.TotalDecisionValue);
        Assert.All(a1.ProjectionEvaluations, p => Assert.All(p.SourceWeeks, w => Assert.True(w < 7)));

        knowledgeState.ConfigurePassthrough();
        var b = await seasonRunner.RunAsync(new MultiWeekReplayRequest
        {
            Season = 2018,
            StartWeek = 7,
            EndWeek = 7,
            FixtureId = "nflverse",
            CandidateUniverse = HistoricalCandidateUniverse.ExpandedSkillUniverse
        });

        // Passthrough should be able to diverge from Baseline when knowledge is present.
        Assert.True(b.DataQuality.PlayersEvaluated > 17);
        Assert.All(b.ProjectionEvaluations, p => Assert.DoesNotContain(7, p.SourceWeeks));

        knowledgeState.ConfigurePassthrough();
    }

    [Fact]
    public async Task Official_Shared_Knowledge_Expanded_Universe_Experiment_Runs_Once()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunSharedKnowledgeExpandedUniverseExperimentAsync(provider);

        Assert.Equal(FrozenSharedKnowledgeExpandedUniverseExperimentV1.ExperimentId, report.ExperimentId);
        Assert.Equal(HistoricalCandidateUniverse.ExpandedSkillUniverse, report.CandidateUniverse);
        Assert.False(report.UsedHoldoutDuringFitting);
        Assert.True(report.ProjectionV2Unchanged);
        Assert.True(report.ConfidenceV2Unchanged);
        Assert.True(report.DecisionPolicyV1Unchanged);
        Assert.True(report.RejectedTransformsRemainDisabled);
        Assert.Equal(2024, report.HoldoutSeason);
        Assert.Equal(new[] { 2015, 2018, 2021 }, report.DevelopmentSeasons);
        Assert.True(report.DevelopmentStartSitCandidates > 289);
        Assert.True(report.HoldoutStartSitCandidates > 289);
        Assert.True(report.DevelopmentCoverage.UsableKnowledgeRatePercent > 0);
        Assert.True(report.HoldoutQuickPicksBaseline.PredictionsEvaluated > 576);
        Assert.Equal(0, report.HoldoutQuickPicksTreatment.RanksChangedVsControl);

        Assert.True(
            report.Verdict is ProjectionExperimentVerdict.Improvement
                or ProjectionExperimentVerdict.NoMaterialImprovement
                or ProjectionExperimentVerdict.Regression
                or ProjectionExperimentVerdict.Inconclusive);

        var state = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        Assert.Equal(KnowledgeMode.Passthrough, state.Mode);

        var text = report.ToReportText();
        Assert.Contains("ExpandedSkillUniverse", text);
        Assert.Contains("HOLDOUT", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("knowledge coverage", text, StringComparison.OrdinalIgnoreCase);

        var outPath = Path.Combine(AppContext.BaseDirectory, "SHARED_KNOWLEDGE_EXPANDED_UNIVERSE_V1_REPORT.txt");
        await File.WriteAllTextAsync(outPath, text);

        var docsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs"));
        Directory.CreateDirectory(docsDir);
        await File.WriteAllTextAsync(
            Path.Combine(docsDir, "SHARED_KNOWLEDGE_EXPANDED_UNIVERSE_V1_REPORT.txt"),
            text);
        await File.WriteAllTextAsync(
            Path.Combine(docsDir, "SHARED_KNOWLEDGE_EXPANDED_UNIVERSE_EXPERIMENT_V1.md"),
            BuildDoc(report));
    }

    private static string BuildDoc(SharedKnowledgeExpandedUniverseExperimentReport report) =>
        $"""
        # Shared Knowledge × Expanded Universe Experiment V1

        **ExperimentId:** `{report.ExperimentId}`  
        **Date:** {report.GeneratedAt:yyyy-MM-dd}  
        **Status:** Complete — **{report.Verdict}**

        ## Question

        Does assembled shared knowledge (production `Passthrough`) improve prediction/decision quality
        vs stripped `Baseline` when evaluation uses `ExpandedSkillUniverse`?

        ## Protocol

        - Control: `KnowledgeMode.Baseline`, ActiveGroups=None
        - Treatment: `KnowledgeMode.Passthrough`, ActiveGroups=None
        - Universe: `ExpandedSkillUniverse`
        - Dev seasons: 2015/2018/2021 (informational folds; no parameter selection)
        - Holdout: 2024 once
        - Rejected transforms stay off (Usage / RoleHealth / RecentForm / ThinMargin / DataSufficiencyTrust)
        - Projection V2 / Confidence V2 / Decision Policy V1 unchanged
        - Frozen 2018 LabRoster benchmark path untouched

        ## Verdict

        **{report.Verdict}**

        {report.VerdictRationale}

        ## Start/Sit holdout

        | Metric | Baseline | Passthrough | Δ |
        |---|---:|---:|---:|
        | Graded | {report.HoldoutBaseline.GradedDecisions} | {report.HoldoutTreatment.GradedDecisions} | — |
        | Accuracy | {report.HoldoutBaseline.AccuracyPercent:0.0}% | {report.HoldoutTreatment.AccuracyPercent:0.0}% | — |
        | Total DV | {report.HoldoutBaseline.TotalDecisionValue:0.00} | {report.HoldoutTreatment.TotalDecisionValue:0.00} | {(report.HoldoutTreatment.TotalDecisionValue ?? 0) - (report.HoldoutBaseline.TotalDecisionValue ?? 0):0.00} |
        | Change rate | — | {report.HoldoutTreatment.ChangeRatePercent:0.0}% | — |
        | Proj MAE | {report.HoldoutBaseline.ProjectionMae:0.00} | {report.HoldoutTreatment.ProjectionMae:0.00} | — |

        Candidates: {report.HoldoutStartSitCandidates}. Usable knowledge rate: {report.HoldoutCoverage.UsableKnowledgeRatePercent:0.0}%.

        ## Quick Picks holdout

        | Metric | Baseline | Treatment | Δ |
        |---|---:|---:|---:|
        | Predictions | {report.HoldoutQuickPicksBaseline.PredictionsEvaluated} | {report.HoldoutQuickPicksTreatment.PredictionsEvaluated} | — |
        | MAE | {report.HoldoutQuickPicksBaseline.MeanAbsoluteError:0.000} | {report.HoldoutQuickPicksTreatment.MeanAbsoluteError:0.000} | — |
        | Top5 | {report.HoldoutQuickPicksBaseline.Top5HitRate:0.0}% | {report.HoldoutQuickPicksTreatment.Top5HitRate:0.0}% | — |
        | Ranks changed | — | {report.HoldoutQuickPicksTreatment.RanksChangedVsControl} | — |

        ## Production

        Remains `KnowledgeMode.Passthrough`. No rejected transform re-enabled.

        ## Full machine report

        See `SHARED_KNOWLEDGE_EXPANDED_UNIVERSE_V1_REPORT.txt`.
        """;
}
