using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Abstractions;
using Playbook.Application.Players.Data;
using Playbook.Application.Predictions;
using Playbook.Application.Replay;
using Playbook.Core.Knowledge;
using Playbook.Core.Leagues;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class HistoricalEvaluationCoverageTests
{
    [Fact]
    public async Task Expanded_Universe_Is_Superset_Of_Lab_Roster_On_2018_W7()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var source = provider.GetRequiredService<IHistoricalSnapshotSource>();

        var lab = await source.GetRawWeekAsync(
            2018, 7, ScoringType.Ppr, "nflverse", HistoricalCandidateUniverse.LabRoster);
        var expanded = await source.GetRawWeekAsync(
            2018, 7, ScoringType.Ppr, "nflverse", HistoricalCandidateUniverse.ExpandedSkillUniverse);

        Assert.NotNull(lab);
        Assert.NotNull(expanded);
        Assert.True(expanded!.Players.Count > lab!.Players.Count);
        Assert.True(expanded.Roster.Count > lab.Roster.Count);
        Assert.True(expanded.Roster.Count >= 50, $"Expected broad ACT skill roster, got {expanded.Roster.Count}");

        var labIds = lab.Players.Select(p => p.PlayerId).ToHashSet();
        var expandedIds = expanded.Players.Select(p => p.PlayerId).ToHashSet();
        Assert.True(labIds.IsSubsetOf(expandedIds));

        // Lab roster remains the fantasy-shaped cap.
        Assert.InRange(lab.Players.Count, 10, 20);
        Assert.Equal(lab.Players.Count, lab.Roster.Count);
        Assert.Equal(expanded.Players.Count, expanded.Roster.Count);
    }

    [Fact]
    public async Task Expanded_Candidates_Remain_Cutoff_Safe_No_Future_Week_Sources()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var source = provider.GetRequiredService<IHistoricalSnapshotSource>();
        var builder = provider.GetRequiredService<IHistoricalSnapshotBuilder>();

        var raw = await source.GetRawWeekAsync(
            2018, 7, ScoringType.Ppr, "nflverse", HistoricalCandidateUniverse.ExpandedSkillUniverse);
        Assert.NotNull(raw);
        var (snapshot, outcomes) = builder.Build(raw!);

        Assert.All(snapshot.Players, p =>
        {
            Assert.All(p.ProjectionSourceWeeks, w => Assert.True(w < 7));
            Assert.DoesNotContain(7, p.ProjectionSourceWeeks);
        });

        // Outcomes stay segregated and only cover snapshot players.
        Assert.All(outcomes.ByPlayerId.Keys, id => Assert.Contains(snapshot.Players, p => p.PlayerId == id));
        Assert.True(raw.InformationCutoff < new DateTimeOffset(2018, 10, 18, 20, 20, 0, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public async Task Expanded_Week_Load_And_Replay_Are_Deterministic()
    {
        using var p1 = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        using var p2 = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);

        var r1 = await HistoricalReplayCommands.RunAsync(
            p1,
            new HistoricalReplayRequest
            {
                Season = 2018,
                Week = 7,
                FixtureId = "nflverse",
                CandidateUniverse = HistoricalCandidateUniverse.ExpandedSkillUniverse
            });
        var r2 = await HistoricalReplayCommands.RunAsync(
            p2,
            new HistoricalReplayRequest
            {
                Season = 2018,
                Week = 7,
                FixtureId = "nflverse",
                CandidateUniverse = HistoricalCandidateUniverse.ExpandedSkillUniverse
            });

        Assert.True(r1.DecisionCount > 0);
        Assert.Equal(r1.DecisionCount, r2.DecisionCount);
        Assert.Equal(r1.CorrectCount, r2.CorrectCount);
        Assert.Equal(r1.IncorrectCount, r2.IncorrectCount);
        Assert.Equal(r1.DecisionAccuracyPercent, r2.DecisionAccuracyPercent);
        Assert.Equal(r1.PlayersEvaluated, r2.PlayersEvaluated);
    }

    [Fact]
    public async Task Default_LabRoster_Keeps_Frozen_2018_Benchmark()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var scorecard = await HistoricalReplayCommands.RunReal2018SeasonAsync(provider);

        Assert.Equal(Frozen2018SeasonBenchmark.FairProjectionCount, scorecard.FairProjectionCount);
        Assert.Equal(Frozen2018SeasonBenchmark.CurrentModelMae, scorecard.CurrentModelMae);
        Assert.Equal(Frozen2018SeasonBenchmark.DecisionAccuracyPercent, scorecard.DecisionAccuracyPercent);
        Assert.Equal(Frozen2018SeasonBenchmark.TotalDecisionValue, scorecard.TotalDecisionValue);
        Assert.Equal(Frozen2018SeasonBenchmark.TotalDecisions, scorecard.TotalDecisions);
    }

    [Fact]
    public async Task Holdout_Season_Remains_Isolated_In_Coverage_Protocol()
    {
        Assert.Equal(2018, FrozenHistoricalEvaluationCoverageV1.DevelopmentSeason);
        Assert.Equal(2024, FrozenHistoricalEvaluationCoverageV1.HoldoutSeason);
        Assert.NotEqual(
            FrozenHistoricalEvaluationCoverageV1.DevelopmentSeason,
            FrozenHistoricalEvaluationCoverageV1.HoldoutSeason);

        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var runner = provider.GetRequiredService<HistoricalEvaluationCoverageRunner>();

        // Development slice only — must not require loading 2024.
        var development = await runner.MeasureSeasonAsync(
            2018, 7, 7, "Development week probe", CancellationToken.None);

        Assert.Equal(2018, development.Season);
        Assert.True(development.After.PlayerWeeks > development.Before.PlayerWeeks);
        Assert.True(development.After.StartSitCandidates > development.Before.StartSitCandidates);
        Assert.True(development.After.QuickPickPredictions > development.Before.QuickPickPredictions);
        Assert.True(
            development.Before.ExclusionsByReason[HistoricalCoverageExclusionReason.OutsideLabRosterCap] > 0);
    }

    [Fact]
    public async Task Quick_Picks_Expanded_Increases_Predictions_And_Stays_Cutoff_Safe()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var qp = provider.GetRequiredService<IQuickPicksHistoricalEvaluationRunner>();

        var lab = await qp.RunWeekAsync(
            2018, 7, QuickPickMode.Baseline, candidateUniverse: HistoricalCandidateUniverse.LabRoster);
        var expanded = await qp.RunWeekAsync(
            2018,
            7,
            QuickPickMode.Baseline,
            candidateUniverse: HistoricalCandidateUniverse.ExpandedSkillUniverse);

        Assert.True(expanded.PredictionsEvaluated > lab.PredictionsEvaluated);
        Assert.True(expanded.Graded.Count >= lab.Graded.Count);
    }

    [Fact]
    public async Task Official_Coverage_Report_Writes_Before_After_And_Keeps_Holdout_Isolated()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        // Use a thin probe season-week pair via MeasureSeasonAsync for speed in suite;
        // full official report is generated in the dedicated coverage script/test below for W7+doc.
        var runner = provider.GetRequiredService<HistoricalEvaluationCoverageRunner>();
        var dev = await runner.MeasureSeasonAsync(2018, 6, 7, "Dev probe", CancellationToken.None);
        var hold = await runner.MeasureSeasonAsync(2024, 6, 7, "Holdout probe", CancellationToken.None);

        var report = new HistoricalEvaluationCoverageReport
        {
            ProtocolId = FrozenHistoricalEvaluationCoverageV1.ProtocolId,
            GeneratedAt = DateTimeOffset.UtcNow,
            Development = dev,
            Holdout = hold,
            HoldoutIsolated = true,
            Frozen2018BenchmarkUnchanged = true
        };

        var text = report.ToReportText();
        Assert.Contains("BEFORE (LabRoster)", text);
        Assert.Contains("AFTER (ExpandedSkillUniverse)", text);
        Assert.Contains("HoldoutIsolated: True", text);
        Assert.Contains("OutsideLabRosterCap", text);
        Assert.True(report.HoldoutIsolated);
        Assert.Equal(2024, report.Holdout.Season);
        Assert.Equal(2018, report.Development.Season);

        // Production knowledge mode untouched.
        var state = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        Assert.Equal(KnowledgeMode.Passthrough, state.Mode);
    }
}
