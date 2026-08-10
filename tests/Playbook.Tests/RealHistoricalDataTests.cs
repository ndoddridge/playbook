using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Abstractions;
using Playbook.Application.Players.Data;
using Playbook.Application.Replay;
using Playbook.Core.Decisions;
using Playbook.Core.Leagues;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class RealHistoricalDataTests
{
    [Fact]
    public void Identity_Normalizer_Is_Gsis_Stable_And_Rejects_Missing_Gsis()
    {
        var normalizer = new HistoricalPlayerIdentityNormalizer();
        var a = normalizer.Normalize("00-0033873", "Patrick Mahomes", "QB", "KC", 2018, 7);
        var b = normalizer.Normalize("00-0033873", "Patrick Mahomes", "QB", "KC", 2018, 7);
        var c = normalizer.Normalize("00-0033280", "Travis Kelce", "TE", "KC", 2018, 7);

        Assert.Equal(a.PlaybookId, b.PlaybookId);
        Assert.NotEqual(a.PlaybookId, c.PlaybookId);
        Assert.Equal("00-0033873", a.GsisId);
        Assert.Throws<ArgumentException>(() =>
            normalizer.Normalize(" ", "Someone", "QB", "KC", 2018, 7));

        // Same display name + different GSIS must not collide.
        var cloneName = normalizer.Normalize("00-0099999", "Patrick Mahomes", "QB", "KC", 2018, 7);
        Assert.NotEqual(a.PlaybookId, cloneName.PlaybookId);
    }

    [Fact]
    public async Task Real_2018_Week7_Can_Be_Loaded_And_Validated()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var source = provider.GetRequiredService<IHistoricalSnapshotSource>();
        var validator = provider.GetRequiredService<IHistoricalWeekDataValidator>();

        var raw = await source.GetRawWeekAsync(2018, 7, ScoringType.Ppr, fixtureId: "nflverse");
        Assert.NotNull(raw);
        Assert.Equal(2018, raw!.Season);
        Assert.Equal(7, raw.Week);
        Assert.StartsWith("nflverse", raw.SourceLabel, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(raw.Players);
        Assert.NotEmpty(raw.Outcomes);
        Assert.Contains(raw.UnavailableSources, s => s.Contains("projection", StringComparison.OrdinalIgnoreCase));

        // Projections must not be fabricated.
        Assert.All(raw.Players, p => Assert.Null(p.ProjectedPoints));

        validator.ValidateOrThrow(raw);
        Assert.True(raw.InformationCutoff < new DateTimeOffset(2018, 10, 18, 20, 20, 0, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public async Task Real_Snapshot_Excludes_Week7_Outcomes_From_PreGame_Knowledge()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var source = provider.GetRequiredService<IHistoricalSnapshotSource>();
        var builder = provider.GetRequiredService<IHistoricalSnapshotBuilder>();
        var knowledgeFactory = provider.GetRequiredService<IHistoricalKnowledgeFactory>();

        var raw = await source.GetRawWeekAsync(2018, 7, ScoringType.Ppr, "nflverse");
        Assert.NotNull(raw);
        var (snapshot, outcomes) = builder.Build(raw!);
        var knowledge = knowledgeFactory.BuildKnowledge(snapshot, ReplayContext.FromSnapshot(snapshot).DecisionContext);

        Assert.NotEmpty(outcomes.ByPlayerId);
        Assert.All(knowledge, k =>
        {
            Assert.Null(k.ProjectedPoints);
            Assert.DoesNotContain(k.Facts, f => f.Statement.Contains("actual", StringComparison.OrdinalIgnoreCase));
            Assert.True(k.InformationCutoff is null || k.InformationCutoff <= snapshot.InformationCutoff);
        });

        // Outcome points must not appear in pre-game signals.
        foreach (var outcome in outcomes.ByPlayerId.Values)
        {
            var text = outcome.ActualFantasyPoints.ToString("0.0");
            Assert.DoesNotContain(
                knowledge.SelectMany(k => k.Signals.Select(s => s.Explanation)),
                e => e.Contains(text, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Real_Replay_Attaches_Outcomes_Only_After_Decisions()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var source = provider.GetRequiredService<IHistoricalSnapshotSource>();
        var builder = provider.GetRequiredService<IHistoricalSnapshotBuilder>();
        var knowledgeFactory = provider.GetRequiredService<IHistoricalKnowledgeFactory>();
        var engine = provider.GetRequiredService<IDecisionEngine>();
        var store = provider.GetRequiredService<IDecisionRecordStore>();

        var raw = await source.GetRawWeekAsync(2018, 7, ScoringType.Ppr, "nflverse");
        Assert.NotNull(raw);
        var (snapshot, _) = builder.Build(raw!);
        var replay = ReplayContext.FromSnapshot(snapshot);
        var knowledge = knowledgeFactory.BuildKnowledge(snapshot, replay.DecisionContext);
        var candidates = snapshot.Roster.Select(slot =>
        {
            var p = snapshot.Players.First(x => x.PlayerId == slot.PlayerId);
            return new StartSitCandidate
            {
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                Position = p.Position,
                IsStarter = slot.IsStarter
            };
        }).ToList();

        var batch = await engine.EvaluateStartSitAsync(knowledge, candidates, replay.DecisionContext);
        var records = await store.ListAsync(2018, 7);
        Assert.NotEmpty(batch.Decisions);
        Assert.All(records, r =>
        {
            Assert.Null(r.ActualOutcome);
            Assert.Null(r.EvaluationResult);
            Assert.Equal(snapshot.InformationCutoff, r.InformationCutoff);
        });
    }

    [Fact]
    public async Task Real_2018_Week7_Replay_Is_Deterministic_And_Evaluates()
    {
        using var p1 = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var r1 = await HistoricalReplayCommands.RunReal2018Week7Async(p1);

        using var p2 = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var r2 = await HistoricalReplayCommands.RunReal2018Week7Async(p2);

        Assert.True(r1.DecisionCount > 0);
        Assert.Equal(r1.DecisionCount, r2.DecisionCount);
        Assert.Equal(r1.CorrectCount, r2.CorrectCount);
        Assert.Equal(r1.IncorrectCount, r2.IncorrectCount);
        Assert.Equal(r1.DecisionAccuracyPercent, r2.DecisionAccuracyPercent);
        Assert.Equal(r1.AverageConfidence, r2.AverageConfidence);
        Assert.All(r1.DecisionRecords, rec => Assert.NotNull(rec.ActualOutcome));
        Assert.Contains(r1.UnavailableSources, s => s.Contains("projection", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("nflverse", r1.LeagueName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Synthetic_Leakage_Fixture_Still_Works_Alongside_Real_Data()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var source = provider.GetRequiredService<IHistoricalSnapshotSource>();
        var builder = provider.GetRequiredService<IHistoricalSnapshotBuilder>();

        var synthetic = await source.GetRawWeekAsync(2018, 7, ScoringType.Ppr, ControlledHistoricalFixture.FixtureId);
        Assert.NotNull(synthetic);
        Assert.Equal(ControlledHistoricalFixture.FixtureId, synthetic!.SourceLabel);

        var (snapshot, _) = builder.Build(synthetic);
        var delta = snapshot.Players.Single(p => p.PlayerId == ControlledHistoricalFixture.DeltaWrId);
        Assert.Null(delta.InjuryStatus);
        Assert.Null(delta.RecentNewsHeadline);

        var real = await source.GetRawWeekAsync(2018, 7, ScoringType.Ppr, "nflverse");
        Assert.NotNull(real);
        Assert.NotEqual(synthetic.SourceLabel, real!.SourceLabel);
    }

    [Fact]
    public async Task Missing_Historical_Projection_Is_Represented_Honestly()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunReal2018Week7Async(provider);
        Assert.All(report.Grades, g => Assert.Equal(0, g.ExpectedValue));
        Assert.Contains(report.UnavailableSources, s => s.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase));
    }
}
