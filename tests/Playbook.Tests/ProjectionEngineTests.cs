using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.News;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;
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
        Assert.True(status.UniqueProjectionValues > 1);
        Assert.NotNull(status.ProjectionRuntime);
        Assert.True(status.AverageProjectionConfidence > 0);
        Assert.True(status.AverageProjection > 0);
    }

    [Fact]
    public void Named_Stars_Produce_Differentiated_Player_Specific_Projections()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Mock);
        var players = provider.GetRequiredService<IPlayerService>();
        var projections = provider.GetRequiredService<IProjectionService>();

        var names = new[]
        {
            "Patrick Mahomes",
            "Jayden Daniels",
            "Saquon Barkley",
            "Bijan Robinson",
            "Ja'Marr Chase",
            "Travis Kelce"
        };

        var byName = names.Select(name =>
        {
            var player = players.GetAllPlayers().First(p => p.FullName == name);
            var proj = projections.GetProjection(player.Id);
            Assert.NotNull(proj);
            return (Name: name, Proj: proj!);
        }).ToList();

        var distinctPoints = byName.Select(x => x.Proj.ProjectedFantasyPoints).Distinct().Count();
        Assert.True(distinctPoints >= 5, $"Expected differentiated projections, got {distinctPoints}: " +
            string.Join(", ", byName.Select(x => $"{x.Name}={x.Proj.ProjectedFantasyPoints}")));

        // With identical intelligence, production alone must differentiate these stars.
        var engine = new ProjectionEngine(Options.Create(new ProjectionRuleOptions()));
        var productionProvider = provider.GetRequiredService<IPlayerProductionProvider>();
        var league = League(ScoringType.Ppr);
        PlayerProjection NeutralProject(string name)
        {
            var player = players.GetAllPlayers().First(p => p.FullName == name);
            return engine.Project(
                player,
                productionProvider.GetProduction(player),
                Profile(player.Id, health: 70, opportunity: 70, usage: 70, confidence: 75),
                league);
        }

        Assert.True(NeutralProject("Jayden Daniels").ProjectedFantasyPoints >
                    NeutralProject("Patrick Mahomes").ProjectedFantasyPoints);
        Assert.True(NeutralProject("Saquon Barkley").ProjectedFantasyPoints >
                    NeutralProject("Bijan Robinson").ProjectedFantasyPoints);
        Assert.True(NeutralProject("Ja'Marr Chase").ProjectedFantasyPoints >
                    NeutralProject("Travis Kelce").ProjectedFantasyPoints);

        Assert.All(byName, x =>
        {
            Assert.True(x.Proj.Floor < x.Proj.Median);
            Assert.True(x.Proj.Median < x.Proj.Ceiling);
            Assert.InRange(x.Proj.Confidence, 0, 100);
            Assert.Contains(x.Proj.ProjectionReasoning, r => r.Contains("Base projection", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(x.Proj.ProjectionReasoning, r => r.Contains("production", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(x.Proj.SupportingIntelligence, s => s.Contains("Production[", StringComparison.Ordinal));
        });

        // Reasoning must not be identical across players.
        var reasonFingerprints = byName
            .Select(x => string.Join("|", x.Proj.ProjectionReasoning))
            .Distinct()
            .Count();
        Assert.True(reasonFingerprints >= 5);
    }

    [Fact]
    public void Same_Position_Players_Can_Differ()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Mock);
        var players = provider.GetRequiredService<IPlayerService>();
        var projections = provider.GetRequiredService<IProjectionService>();

        var qbs = players.GetAllPlayers().Where(p => p.Position == Position.QB).ToList();
        var qbPoints = qbs
            .Select(p => projections.GetProjection(p.Id)!.ProjectedFantasyPoints)
            .Distinct()
            .Count();
        Assert.True(qbPoints > 1);
    }

    [Fact]
    public void Higher_Opportunity_Produces_Higher_Projection_When_All_Else_Equal()
    {
        var engine = new ProjectionEngine(Options.Create(new ProjectionRuleOptions()));
        var player = SamplePlayer(Position.WR, "Ja'Marr Chase");
        var production = ChaseProduction(player);
        var league = League(ScoringType.Ppr);

        var low = engine.Project(player, production, Profile(player.Id, opportunity: 30, health: 70, usage: 60), league);
        var high = engine.Project(player, production, Profile(player.Id, opportunity: 85, health: 70, usage: 60), league);

        Assert.True(high.ProjectedFantasyPoints > low.ProjectedFantasyPoints);
        Assert.Contains(high.ProjectionReasoning, r => r.Contains("Opportunity score 85", StringComparison.Ordinal));
    }

    [Fact]
    public void Health_Concern_Reduces_Projection_And_Increases_Downside()
    {
        var engine = new ProjectionEngine(Options.Create(new ProjectionRuleOptions()));
        var player = SamplePlayer(Position.RB, "Saquon Barkley");
        var production = BarkleyProduction(player);
        var league = League(ScoringType.Ppr);

        var healthy = engine.Project(player, production, Profile(player.Id, health: 90, opportunity: 70, usage: 70), league);
        var hurt = engine.Project(player, production, Profile(player.Id, health: 25, opportunity: 70, usage: 70), league);

        Assert.True(hurt.ProjectedFantasyPoints < healthy.ProjectedFantasyPoints);
        Assert.True((healthy.Median - healthy.Floor) <= (hurt.Median - hurt.Floor) + 0.05m
            || hurt.Floor < healthy.Floor);
        Assert.Contains(hurt.ProjectionReasoning, r => r.Contains("Health score 25", StringComparison.Ordinal));
    }

    [Fact]
    public void Stronger_Historical_Production_Produces_Higher_Baseline()
    {
        var engine = new ProjectionEngine(Options.Create(new ProjectionRuleOptions()));
        var league = League(ScoringType.Ppr);

        var elite = SamplePlayer(Position.WR, "Elite Receiver", Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var eliteProd = new PlayerProductionSnapshot
        {
            PlayerId = elite.Id,
            PlayerName = elite.FullName,
            Position = Position.WR,
            Season = 2024,
            Source = ProductionDataSource.CuratedSeason,
            SourceDescription = "Test elite production",
            GamesPlayed = 17,
            Targets = 175,
            Receptions = 127,
            ReceivingYards = 1708,
            ReceivingTouchdowns = 17
        };

        var depth = SamplePlayer(Position.WR, "Depth Receiver", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var depthProd = new PlayerProductionSnapshot
        {
            PlayerId = depth.Id,
            PlayerName = depth.FullName,
            Position = Position.WR,
            Season = 2024,
            Source = ProductionDataSource.CuratedSeason,
            SourceDescription = "Test depth production",
            GamesPlayed = 17,
            Targets = 70,
            Receptions = 40,
            ReceivingYards = 520,
            ReceivingTouchdowns = 3
        };

        var eliteProj = engine.Project(
            elite,
            eliteProd,
            Profile(elite.Id, health: 50, opportunity: 50, usage: 50, confidence: 70),
            league);
        var depthProj = engine.Project(
            depth,
            depthProd,
            Profile(depth.Id, health: 50, opportunity: 50, usage: 50, confidence: 70),
            league);

        Assert.True(eliteProj.ProjectedFantasyPoints > depthProj.ProjectedFantasyPoints);
    }

    [Fact]
    public void Ppr_And_HalfPpr_Differ_When_Receiving_Matters()
    {
        var engine = new ProjectionEngine(Options.Create(new ProjectionRuleOptions()));
        var player = SamplePlayer(Position.WR, "Ja'Marr Chase");
        var production = ChaseProduction(player);
        var intel = Profile(player.Id, health: 70, opportunity: 70, usage: 70);

        var ppr = engine.Project(player, production, intel, League(ScoringType.Ppr));
        var half = engine.Project(player, production, intel, League(ScoringType.HalfPpr));
        var std = engine.Project(player, production, intel, League(ScoringType.Standard));

        Assert.True(ppr.ProjectedFantasyPoints > half.ProjectedFantasyPoints);
        Assert.True(half.ProjectedFantasyPoints > std.ProjectedFantasyPoints);
    }

    [Fact]
    public void Floor_Median_Ceiling_And_Confidence_Bounds()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Mock);
        var projections = provider.GetRequiredService<IProjectionService>().GetAllProjections();

        Assert.All(projections, p =>
        {
            Assert.True(p.Floor < p.Median, $"{p.PlayerId}: floor {p.Floor} !< median {p.Median}");
            Assert.True(p.Median < p.Ceiling, $"{p.PlayerId}: median {p.Median} !< ceiling {p.Ceiling}");
            Assert.InRange(p.Confidence, 0, 100);
        });
    }

    [Fact]
    public void Josh_Allen_Curated_Production_Differs_From_Mahomes()
    {
        var productionProvider = new PlayerProductionProvider(new StubPlayerService(), new StubPlayerStatsService());
        var mahomes = SamplePlayer(Position.QB, "Patrick Mahomes", Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var allen = SamplePlayer(Position.QB, "Josh Allen", Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

        var engine = new ProjectionEngine(Options.Create(new ProjectionRuleOptions()));
        var league = League(ScoringType.Ppr);

        var mahomesProj = engine.Project(
            mahomes,
            productionProvider.GetProduction(mahomes),
            Profile(mahomes.Id, health: 70, opportunity: 70, usage: 70, confidence: 75),
            league);
        var allenProj = engine.Project(
            allen,
            productionProvider.GetProduction(allen),
            Profile(allen.Id, health: 70, opportunity: 70, usage: 70, confidence: 75),
            league);

        Assert.NotEqual(mahomesProj.ProjectedFantasyPoints, allenProj.ProjectedFantasyPoints);
        Assert.True(allenProj.ProjectedFantasyPoints > mahomesProj.ProjectedFantasyPoints);
    }

    [Fact]
    public void League_Scoring_Change_Refreshes_Projections()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Mock);
        var leagueState = provider.GetRequiredService<ILeagueState>();
        var projections = provider.GetRequiredService<IProjectionService>();
        var players = provider.GetRequiredService<IPlayerService>();
        var chase = players.GetAllPlayers().First(p => p.FullName == "Ja'Marr Chase");

        // Default mock league is PPR (Friends League).
        var pprPoints = projections.GetProjection(chase.Id)!.ProjectedFantasyPoints;

        var halfPprLeague = leagueState.GetAllLeagues().First(l => l.ScoringType == ScoringType.HalfPpr);
        leagueState.SelectLeague(halfPprLeague.Id);
        projections.Refresh();

        var halfPoints = projections.GetProjection(chase.Id)!.ProjectedFantasyPoints;
        Assert.True(pprPoints > halfPoints);
    }

    private static ProjectionLeagueContext League(ScoringType scoring) => new()
    {
        LeagueName = "Test",
        ScoringType = scoring,
        CurrentWeek = 1,
        Season = 2026,
        NumberOfTeams = 12
    };

    private static Player SamplePlayer(
        Position position,
        string fullName,
        Guid? id = null)
    {
        var parts = fullName.Split(' ', 2);
        return new Player
        {
            Id = id ?? Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            FullName = fullName,
            FirstName = parts[0],
            LastName = parts.Length > 1 ? parts[1] : parts[0],
            Position = position,
            Team = "TEST",
            Status = PlayerStatus.Active,
            YearsPro = 5,
            Age = 27
        };
    }

    private static PlayerProductionSnapshot ChaseProduction(Player player) => new()
    {
        PlayerId = player.Id,
        PlayerName = player.FullName,
        Position = Position.WR,
        Season = 2024,
        Source = ProductionDataSource.CuratedSeason,
        SourceDescription = "Curated 2024 season production for Ja'Marr Chase.",
        GamesPlayed = 17,
        Targets = 175,
        Receptions = 127,
        ReceivingYards = 1708,
        ReceivingTouchdowns = 17
    };

    private static PlayerProductionSnapshot BarkleyProduction(Player player) => new()
    {
        PlayerId = player.Id,
        PlayerName = player.FullName,
        Position = Position.RB,
        Season = 2024,
        Source = ProductionDataSource.CuratedSeason,
        SourceDescription = "Curated 2024 season production for Saquon Barkley.",
        GamesPlayed = 16,
        RushingAttempts = 345,
        RushingYards = 2005,
        RushingTouchdowns = 13,
        Targets = 43,
        Receptions = 33,
        ReceivingYards = 278,
        ReceivingTouchdowns = 2
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
            OverallRisk = 0,
            OpportunityScore = opportunity,
            TrendDirection = TrendDirection.Flat,
            HealthScore = health,
            UsageScore = usage,
            NewsMomentum = 20,
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

    private sealed class StubPlayerService : IPlayerService
    {
        public IReadOnlyList<Player> GetAllPlayers() => [];
        public Player? GetPlayer(Guid playerId) => null;
        public PlayerProfile? GetPlayerProfile(Guid playerId) => null;
        public IReadOnlyList<Player> SearchPlayers(string? query) => [];
        public void Refresh() { }
    }

    private sealed class StubPlayerStatsService : IPlayerStatsService
    {
        public int GameLogCount => 0;
        public IReadOnlyList<PlayerSeasonStats> GetAllStats() => [];
        public IReadOnlyList<PlayerGameStats> GetAllGameLogs() => [];
        public IReadOnlyList<PlayerSeasonStats> GetStatsForPlayer(Guid playerId) => [];
        public IReadOnlyList<PlayerGameStats> GetGameLogsForPlayer(Guid playerId) => [];
        public IReadOnlyList<PlayerGameStats> GetRecentGameLogs(Guid playerId, int maxGames = 8) => [];
        public IReadOnlyList<int> GetAvailableSeasons(Guid playerId) => [];
        public PlayerSeasonStats? GetStats(Guid playerId, int season, StatsPeriod? period = null) => null;
        public PlayerSeasonStats? GetCareerTotals(Guid playerId) => null;
        public PlayerSeasonStats? GetPrimaryProductionSeason(Guid playerId) => null;
        public IReadOnlyList<PlayerSeasonStats> GetRecentNflSeasons(Guid playerId, int maxSeasons = 3) => [];
        public void Refresh() { }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshCurrentSeasonAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
