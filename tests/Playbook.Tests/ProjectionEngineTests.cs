using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.News;
using Playbook.Application.Players.Data;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Infrastructure.Projections.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Playbook.Tests;

public class ProjectionEngineTests
{
    [Fact]
    public void ProjectionService_Projects_All_Players_Deterministically()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Mock);
        var projections = provider.GetRequiredService<IProjectionService>();
        var status = provider.GetRequiredService<IProjectionSyncStatus>();

        var first = projections.GetAllProjections();
        projections.Refresh();
        var second = projections.GetAllProjections();

        Assert.NotEmpty(first);
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(
            first.Select(p => (p.PlayerId, p.ProjectedFantasyPoints, p.Floor, p.Ceiling, p.Confidence)),
            second.Select(p => (p.PlayerId, p.ProjectedFantasyPoints, p.Floor, p.Ceiling, p.Confidence)));
        Assert.True(status.PlayersProjected > 0);
        Assert.NotNull(status.ProjectionRuntime);
        Assert.True(status.AverageProjectionConfidence > 0);
    }

    [Fact]
    public void Projection_Scores_Differ_By_Player_And_Cite_Intelligence()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Mock);
        var projections = provider.GetRequiredService<IProjectionService>();
        var intelligence = provider.GetRequiredService<IIntelligenceService>();

        var all = projections.GetAllProjections();
        Assert.True(all.Select(p => p.ProjectedFantasyPoints).Distinct().Count() > 1);

        var withIntel = all.FirstOrDefault(p => intelligence.GetPlayerProfile(p.PlayerId) is not null);
        Assert.NotNull(withIntel);
        Assert.NotEmpty(withIntel!.ProjectionReasoning);
        Assert.Contains(withIntel.SupportingIntelligence, s =>
            s.Contains("Health", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Headline", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(withIntel.ProjectionReasoning, r =>
            r.Contains("Health", StringComparison.OrdinalIgnoreCase) ||
            r.Contains("Opportunity", StringComparison.OrdinalIgnoreCase) ||
            r.Contains("Usage", StringComparison.OrdinalIgnoreCase) ||
            r.Contains("Base", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Higher_Health_And_Opportunity_Increase_Projection()
    {
        var engine = new ProjectionEngine(Options.Create(new ProjectionRuleOptions()));
        var player = SamplePlayer(Position.WR);
        var league = new ProjectionLeagueContext
        {
            LeagueName = "Test",
            ScoringType = ScoringType.Ppr,
            CurrentWeek = 1,
            Season = 2026,
            NumberOfTeams = 12
        };

        var low = engine.Project(player, Profile(player.Id, health: 30, opportunity: 30, usage: 50), league);
        var high = engine.Project(player, Profile(player.Id, health: 80, opportunity: 80, usage: 50), league);

        Assert.True(high.ProjectedFantasyPoints > low.ProjectedFantasyPoints);
    }

    [Fact]
    public void Positive_Usage_Raises_Ceiling_And_Low_Confidence_Raises_Volatility()
    {
        var engine = new ProjectionEngine(Options.Create(new ProjectionRuleOptions()));
        var player = SamplePlayer(Position.RB);
        var league = new ProjectionLeagueContext
        {
            LeagueName = "Test",
            ScoringType = ScoringType.HalfPpr,
            CurrentWeek = 3,
            Season = 2026,
            NumberOfTeams = 10
        };

        var lowUsage = engine.Project(player, Profile(player.Id, usage: 40, confidence: 80), league);
        var highUsage = engine.Project(player, Profile(player.Id, usage: 85, confidence: 80), league);
        Assert.True(highUsage.Ceiling > lowUsage.Ceiling);
        Assert.Contains(highUsage.ProjectionReasoning, r => r.Contains("Usage", StringComparison.OrdinalIgnoreCase));

        var highConf = engine.Project(player, Profile(player.Id, confidence: 90), league);
        var lowConf = engine.Project(player, Profile(player.Id, confidence: 35), league);
        Assert.True(lowConf.Volatility > highConf.Volatility);
        Assert.Contains(lowConf.ProjectionReasoning, r => r.Contains("Low Confidence", StringComparison.OrdinalIgnoreCase));
    }

    private static Player SamplePlayer(Position position) => new()
    {
        Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        FullName = "Test Player",
        FirstName = "Test",
        LastName = "Player",
        Position = position,
        Team = "TB",
        Status = PlayerStatus.Active
    };

    private static PlayerIntelligenceProfile Profile(
        Guid playerId,
        int health = 50,
        int opportunity = 50,
        int usage = 50,
        int confidence = 70) =>
        new()
        {
            PlayerId = playerId,
            OverallConfidence = confidence,
            OverallRisk = 50,
            OpportunityScore = opportunity,
            TrendDirection = TrendDirection.Flat,
            HealthScore = health,
            UsageScore = usage,
            NewsMomentum = 50,
            LastUpdated = DateTimeOffset.UtcNow,
            SupportingFacts =
            [
                new IntelligenceFact
                {
                    Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                    Title = "Sample usage note",
                    Description = "Test fact",
                    Category = IntelligenceCategory.Usage,
                    Importance = IntelligenceImportance.Medium,
                    Confidence = 70,
                    Source = IntelligenceSource.News,
                    RelatedPlayerId = playerId,
                    SupportingEvidence = ["Rule: usage-snap"],
                    RelatedNewsArticleIds = [],
                    Created = DateTimeOffset.UtcNow
                }
            ],
            Headline = "Opportunity Increasing",
            ChangeSignal = IntelligenceChangeSignal.OpportunityIncreasing
        };
}
