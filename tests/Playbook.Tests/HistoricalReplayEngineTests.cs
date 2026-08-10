using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Abstractions;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Players.Data;
using Playbook.Application.Replay;
using Playbook.Core.Decisions;
using Playbook.Core.Leagues;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class HistoricalReplayEngineTests
{
    [Fact]
    public void Snapshot_Builder_Strips_Future_Injury_And_News()
    {
        var raw = ControlledHistoricalFixture.Create();
        var builder = new HistoricalSnapshotBuilder();
        var (snapshot, outcomes) = builder.Build(raw);

        Assert.Equal(ControlledHistoricalFixture.Season, snapshot.Season);
        Assert.Equal(ControlledHistoricalFixture.Week, snapshot.Week);
        Assert.Equal(ControlledHistoricalFixture.InformationCutoff, snapshot.InformationCutoff);

        var delta = snapshot.Players.Single(p => p.PlayerId == ControlledHistoricalFixture.DeltaWrId);
        Assert.Null(delta.InjuryStatus);
        Assert.Null(delta.RecentNewsHeadline);
        Assert.Contains(delta.UnavailableSignals, s => s.Contains("Injury", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(delta.UnavailableSignals, s => s.Contains("News", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Healthy", delta.HealthLabel);

        // Echo's pre-cutoff questionable designation remains visible.
        var echo = snapshot.Players.Single(p => p.PlayerId == ControlledHistoricalFixture.EchoTeId);
        Assert.Equal("Questionable", echo.InjuryStatus);

        // Outcomes exist but are segregated from the snapshot object graph.
        Assert.True(outcomes.ByPlayerId.ContainsKey(ControlledHistoricalFixture.AlphaRbId));
        Assert.DoesNotContain(
            snapshot.Players.SelectMany(p => p.UnavailableSignals),
            s => s.Contains("8.1", StringComparison.Ordinal));
    }

    [Fact]
    public void Knowledge_Factory_Cannot_See_Future_Delta_Injury()
    {
        var raw = ControlledHistoricalFixture.Create();
        var (snapshot, _) = new HistoricalSnapshotBuilder().Build(raw);
        var context = ReplayContext.FromSnapshot(snapshot).DecisionContext;
        var knowledge = new HistoricalKnowledgeFactory().BuildKnowledge(snapshot, context);

        var delta = knowledge.Single(k => k.PlayerId == ControlledHistoricalFixture.DeltaWrId);
        Assert.DoesNotContain(delta.Facts, f => f.Statement.Contains("Out", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(delta.Signals, s => s.Explanation.Contains("Hamstring", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(delta.Signals, s => s.Explanation.Contains("ruled out for the season", StringComparison.OrdinalIgnoreCase));
        Assert.All(delta.Facts.Where(f => f.ObservedAt is not null), f => Assert.True(f.ObservedAt <= snapshot.InformationCutoff));
        Assert.All(delta.Signals.Where(s => s.ObservedAt is not null), s => Assert.True(s.ObservedAt <= snapshot.InformationCutoff));
    }

    [Fact]
    public async Task Replay_Runner_Produces_Decisions_Then_Attaches_Outcomes()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var runner = provider.GetRequiredService<IHistoricalReplayRunner>();

        var report = await runner.RunAsync(new HistoricalReplayRequest
        {
            Season = 2018,
            Week = 7,
            ScoringType = ScoringType.Ppr,
            FixtureId = ControlledHistoricalFixture.FixtureId
        });

        Assert.Equal(2018, report.Season);
        Assert.Equal(7, report.Week);
        Assert.True(report.DecisionCount > 0);
        Assert.NotEmpty(report.Grades);
        Assert.NotEmpty(report.DecisionRecords);
        Assert.All(report.Grades, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g.EvaluationSummary));
            Assert.NotEmpty(g.Rationale);
        });
        Assert.All(report.DecisionRecords, r =>
        {
            Assert.Equal(ControlledHistoricalFixture.InformationCutoff, r.InformationCutoff);
            Assert.NotNull(r.ActualOutcome);
            Assert.False(string.IsNullOrWhiteSpace(r.EvaluationResult));
            Assert.NotEmpty(r.SupportingEvidence.Concat(r.Rationale));
        });

        Assert.False(string.IsNullOrWhiteSpace(report.ToSummaryText()));
        Assert.Contains("Replay: 2018 Week 7", report.ToSummaryText());
    }

    [Fact]
    public async Task Comparative_StartSit_Grades_Alpha_Vs_Bravo_Incorrect_When_Alternative_Wins()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunControlled2018Week7Async(provider);

        var alphaStart = report.Grades.FirstOrDefault(g =>
            g.PlayerId == ControlledHistoricalFixture.AlphaRbId &&
            g.Recommendation == DecisionRecommendation.Start);

        Assert.NotNull(alphaStart);
        Assert.Equal(ControlledHistoricalFixture.BravoRbId, alphaStart!.AlternativePlayerId);
        Assert.Equal(15.2, alphaStart.ExpectedValue, 1);
        Assert.Equal(8.1, alphaStart.ActualFantasyPoints);
        Assert.Equal(17.3, alphaStart.AlternativeActualFantasyPoints);
        Assert.False(alphaStart.WasCorrect);
        Assert.True(alphaStart.ActualDecisionDifferential < 0);
        Assert.Contains("INCORRECT", alphaStart.EvaluationSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Information_Leakage_Regression_Future_Events_Never_Enter_Decision_Evidence()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var runner = provider.GetRequiredService<IHistoricalReplayRunner>();
        var report = await runner.RunAsync(new HistoricalReplayRequest
        {
            Season = 2018,
            Week = 7,
            FixtureId = ControlledHistoricalFixture.FixtureId
        });

        var deltaRecords = report.DecisionRecords
            .Where(r => r.PlayerId == ControlledHistoricalFixture.DeltaWrId)
            .ToList();

        foreach (var record in deltaRecords)
        {
            Assert.DoesNotContain(record.SupportingEvidence, e => ContainsFutureLeak(e));
            Assert.DoesNotContain(record.OpposingEvidence, e => ContainsFutureLeak(e));
            Assert.DoesNotContain(record.Rationale, e => ContainsFutureLeak(e));
            Assert.DoesNotContain(record.Unknowns, e => ContainsFutureLeak(e));
        }

        foreach (var grade in report.Grades.Where(g => g.PlayerId == ControlledHistoricalFixture.DeltaWrId))
        {
            Assert.DoesNotContain(grade.SupportingEvidence, ContainsFutureLeak);
            Assert.DoesNotContain(grade.OpposingEvidence, ContainsFutureLeak);
            Assert.DoesNotContain(grade.Rationale, ContainsFutureLeak);
        }

        static bool ContainsFutureLeak(string text) =>
            text.Contains("ruled out for the season", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Hamstring", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Week 8", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replay_Is_Deterministic_On_Grading_Metrics()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var first = await HistoricalReplayCommands.RunControlled2018Week7Async(provider);

        using var provider2 = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var second = await HistoricalReplayCommands.RunControlled2018Week7Async(provider2);

        Assert.Equal(first.DecisionCount, second.DecisionCount);
        Assert.Equal(first.CorrectCount, second.CorrectCount);
        Assert.Equal(first.IncorrectCount, second.IncorrectCount);
        Assert.Equal(first.DecisionAccuracyPercent, second.DecisionAccuracyPercent);
        Assert.Equal(first.AverageProjectionAbsoluteError, second.AverageProjectionAbsoluteError);
    }

    [Fact]
    public async Task Outcomes_Are_Attached_Only_After_Decision_Recording()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var store = provider.GetRequiredService<IDecisionRecordStore>();
        var source = provider.GetRequiredService<IHistoricalSnapshotSource>();
        var builder = provider.GetRequiredService<IHistoricalSnapshotBuilder>();
        var knowledgeFactory = provider.GetRequiredService<IHistoricalKnowledgeFactory>();
        var engine = provider.GetRequiredService<IDecisionEngine>();

        var raw = await source.GetRawWeekAsync(2018, 7, ScoringType.Ppr, ControlledHistoricalFixture.FixtureId);
        Assert.NotNull(raw);
        var (snapshot, outcomes) = builder.Build(raw!);
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
        });

        // Reveal only now.
        Assert.Equal(8.1, outcomes.ByPlayerId[ControlledHistoricalFixture.AlphaRbId].ActualFantasyPoints);
    }

    [Fact]
    public async Task Live_MyTeam_Still_Works_Alongside_Replay_Registration()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var teamIntel = provider.GetRequiredService<IFantasyTeamIntelligenceService>();
        var report = teamIntel.GetReport();
        Assert.True(report.HasRosterPlayers);

        var replay = await HistoricalReplayCommands.RunControlled2018Week7Async(provider);
        Assert.True(replay.DecisionCount > 0);

        // Live report remains usable after replay execution.
        var again = teamIntel.GetReport();
        Assert.True(again.HasRosterPlayers);
        Assert.Equal(report.LeagueId, again.LeagueId);
    }

    [Fact]
    public void Data_Availability_Assessment_Marks_Unavailable_Domains()
    {
        var items = HistoricalDataAvailabilityAssessment.Current;
        Assert.Contains(items, i => i.Domain.Contains("News", StringComparison.OrdinalIgnoreCase) &&
                                    i.Status == HistoricalDataAvailability.Unavailable);
        Assert.Contains(items, i => i.Domain.Contains("roster", StringComparison.OrdinalIgnoreCase) &&
                                    i.Status == HistoricalDataAvailability.Unavailable);
        Assert.Contains(items, i => i.Status == HistoricalDataAvailability.Available);
        Assert.Contains(items, i => i.Status == HistoricalDataAvailability.Partial);
    }
}
