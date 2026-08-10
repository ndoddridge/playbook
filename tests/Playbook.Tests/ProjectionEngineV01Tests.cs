using Playbook.Application.Players;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;
using Playbook.Infrastructure.Projections.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Playbook.Tests;

public class ProjectionEngineV01Tests
{
    [Fact]
    public void Engine_Version_Is_0_1()
    {
        var engine = new ProjectionEngine(Options.Create(new ProjectionRuleOptions()));
        Assert.Equal(ProjectionEngineVersions.V0_1, engine.Version);
        Assert.Equal("0.1", ProjectionEngineVersions.Current);
    }

    [Fact]
    public void Projection_Model_Includes_Metadata_And_Inputs()
    {
        var engine = CreateEngine();
        var player = VeteranRb();
        var proj = engine.Project(player, StableProduction(player), Intel(player.Id), League());

        Assert.Equal(ProjectionEngineVersions.V0_1, proj.ProjectionVersion);
        Assert.Equal(ScoringType.Ppr, proj.ScoringFormat);
        Assert.Equal(1, proj.Week);
        Assert.NotNull(proj.InputsUsed);
        Assert.True(proj.InputsUsed.LeagueScoring);
        Assert.Contains("Matchup", proj.InputsUsed.UnavailableInputs);
        Assert.Contains("Game environment", proj.InputsUsed.UnavailableInputs);
        Assert.Equal(proj.ProjectedFantasyPoints, proj.ProjectedPoints);
        Assert.Equal(proj.ProjectionTimestamp, proj.LastUpdated);
        Assert.True(proj.Floor <= proj.Median);
        Assert.True(proj.Median <= proj.Ceiling);
        Assert.InRange(proj.Confidence, 0, 100);
        Assert.InRange(proj.Volatility, 0, 100);
    }

    [Fact]
    public void Reasoning_Contains_Explicit_Signal_Lines()
    {
        var engine = CreateEngine();
        var player = VeteranRb();
        var proj = engine.Project(
            player,
            StableProduction(player),
            Intel(player.Id, opportunity: 80, usage: 75, health: 50),
            League());

        Assert.Contains(proj.ProjectionReasoning, r => r.StartsWith("Base projection:", StringComparison.Ordinal));
        Assert.Contains(proj.ProjectionReasoning, r => r.StartsWith("Recent usage:", StringComparison.Ordinal));
        Assert.Contains(proj.ProjectionReasoning, r => r.StartsWith("Opportunity", StringComparison.Ordinal));
        Assert.Contains(proj.ProjectionReasoning, r => r.Contains("Health: looks about average", StringComparison.Ordinal));
        Assert.Contains(proj.ProjectionReasoning, r => r.Contains("Matchup: unavailable", StringComparison.Ordinal));
        Assert.Contains(proj.ProjectionReasoning, r => r.Contains("Game environment: unavailable", StringComparison.Ordinal));
        Assert.Contains(proj.ProjectionReasoning, r => r.StartsWith("Confidence:", StringComparison.Ordinal));
        Assert.Contains(proj.ProjectionReasoning, r => r.StartsWith("Volatility:", StringComparison.Ordinal));
        Assert.Contains(proj.ProjectionReasoning, r => r.Contains("Projection Engine v0.1", StringComparison.Ordinal));
    }

    [Fact]
    public void Stable_Veteran_Has_Higher_Confidence_Than_Limited_Sample_Rookie()
    {
        var engine = CreateEngine();
        var veteran = VeteranRb();
        var rookie = RookieWr();

        var vetCtx = new PlayerStatisticalContext
        {
            PlayerId = veteran.Id,
            AsOf = DateTimeOffset.UtcNow,
            GameLogsAvailable = 32,
            NflSeasonsAvailable = 5,
            Trend = StatisticalTrendSignal.Stable,
            RecentProduction = Window(FootballLevel.Nfl, "Recent", 8, rushAtt: 18, rushYd: 85, tgt: 4, rec: 3, recYd: 25)
        };
        var rookCtx = new PlayerStatisticalContext
        {
            PlayerId = rookie.Id,
            AsOf = DateTimeOffset.UtcNow,
            GameLogsAvailable = 2,
            NflSeasonsAvailable = 0,
            HasCollegeStatistics = true,
            Trend = StatisticalTrendSignal.Unknown,
            CollegeProduction = Window(FootballLevel.College, "College", 12, tgt: 9, rec: 6, recYd: 90, recTd: 1)
        };

        var vetProj = engine.Project(veteran, StableProduction(veteran), Intel(veteran.Id), League(),
            statisticalContext: vetCtx);
        var rookProj = engine.Project(rookie, LimitedRookieProduction(rookie), Intel(rookie.Id), League(),
            statisticalContext: rookCtx);

        Assert.True(vetProj.Confidence > rookProj.Confidence);
        Assert.True(rookProj.InputsUsed.CollegeStatistics);
        Assert.Contains(rookProj.ProjectionReasoning, r => r.Contains("College", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void High_Volatility_Player_Has_Wider_Range()
    {
        var engine = CreateEngine();
        var player = SamplePlayer(Position.WR, "Boom Bust", yearsPro: 3);
        var volatileCtx = new PlayerStatisticalContext
        {
            PlayerId = player.Id,
            AsOf = DateTimeOffset.UtcNow,
            GameLogsAvailable = 3,
            NflSeasonsAvailable = 2,
            Trend = StatisticalTrendSignal.Volatile,
            Consistency = new StatisticalConsistencySignals { CoefficientOfVariation = 0.8m }
        };

        var volatileProj = engine.Project(
            player,
            TdHeavyProduction(player),
            Intel(player.Id, usage: 85, opportunity: 40, health: 40, risk: 40),
            League(),
            statisticalContext: volatileCtx);

        var stable = VeteranRb();
        var stableProj = engine.Project(
            stable,
            StableProduction(stable),
            Intel(stable.Id),
            League(),
            statisticalContext: new PlayerStatisticalContext
            {
                PlayerId = stable.Id,
                AsOf = DateTimeOffset.UtcNow,
                GameLogsAvailable = 30,
                NflSeasonsAvailable = 6,
                Trend = StatisticalTrendSignal.Stable
            });

        Assert.True(volatileProj.Volatility > stableProj.Volatility);
        var volatileWidth = volatileProj.Ceiling - volatileProj.Floor;
        var stableWidth = stableProj.Ceiling - stableProj.Floor;
        Assert.True(volatileWidth >= stableWidth);
    }

    [Fact]
    public void Injured_Player_Projects_Lower_With_Wider_Floor()
    {
        var engine = CreateEngine();
        var player = VeteranRb();
        var production = StableProduction(player);
        var healthy = engine.Project(player, production, Intel(player.Id), League());
        var injured = engine.Project(
            player,
            production,
            Intel(player.Id, health: 30, risk: 35),
            League(),
            currentInjury: new PlayerInjuryRecord
            {
                PlayerId = player.Id,
                Date = DateTimeOffset.UtcNow,
                Status = "Questionable",
                BodyPart = "Ankle",
                Level = InjuryCompetitionLevel.Nfl,
                Source = "test",
                IsCurrent = true
            });

        Assert.True(injured.ProjectedFantasyPoints < healthy.ProjectedFantasyPoints);
        Assert.True(injured.Volatility >= healthy.Volatility);
        Assert.Contains(injured.ProjectionReasoning, r => r.Contains("Injury signal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Increasing_Usage_Projects_Higher_Than_Declining_Usage()
    {
        var engine = CreateEngine();
        var player = VeteranRb();
        var production = StableProduction(player);

        var increasing = engine.Project(
            player,
            production,
            Intel(player.Id, usage: 80, opportunity: 75, trend: TrendDirection.Up),
            League(),
            statisticalContext: Ctx(player.Id, StatisticalTrendSignal.Increasing, games: 8));

        var declining = engine.Project(
            player,
            production,
            Intel(player.Id, usage: 35, opportunity: 30, trend: TrendDirection.Down),
            League(),
            statisticalContext: Ctx(player.Id, StatisticalTrendSignal.Decreasing, games: 8));

        Assert.True(increasing.ProjectedFantasyPoints > declining.ProjectedFantasyPoints);
        Assert.Contains(increasing.ProjectionReasoning, r => r.Contains("increasing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(declining.ProjectionReasoning, r => r.Contains("decreasing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scoring_Formats_Differ_When_Receptions_Matter()
    {
        var engine = CreateEngine();
        var player = SamplePlayer(Position.WR, "Target Hog", yearsPro: 4);
        var production = new PlayerProductionSnapshot
        {
            PlayerId = player.Id,
            PlayerName = player.FullName,
            Position = Position.WR,
            Season = 2024,
            Source = ProductionDataSource.StatsService,
            SourceDescription = "Real historical data",
            GamesPlayed = 16,
            Targets = 140,
            Receptions = 100,
            ReceivingYards = 1200,
            ReceivingTouchdowns = 8
        };

        var ppr = engine.Project(player, production, Intel(player.Id), League(ScoringType.Ppr));
        var half = engine.Project(player, production, Intel(player.Id), League(ScoringType.HalfPpr));
        var std = engine.Project(player, production, Intel(player.Id), League(ScoringType.Standard));

        Assert.True(ppr.ProjectedFantasyPoints > half.ProjectedFantasyPoints);
        Assert.True(half.ProjectedFantasyPoints > std.ProjectedFantasyPoints);
        Assert.Equal(ScoringType.Ppr, ppr.ScoringFormat);
        Assert.Equal(ScoringType.HalfPpr, half.ScoringFormat);
        Assert.Equal(ScoringType.Standard, std.ScoringFormat);
    }

    [Fact]
    public void Intelligence_Opportunity_Change_Moves_Projection()
    {
        var engine = CreateEngine();
        var player = VeteranRb();
        var production = StableProduction(player);
        var low = engine.Project(player, production, Intel(player.Id, opportunity: 25), League());
        var high = engine.Project(player, production, Intel(player.Id, opportunity: 90), League());
        Assert.True(high.ProjectedFantasyPoints > low.ProjectedFantasyPoints);
    }

    [Fact]
    public void Missing_Optional_Matchup_And_Environment_Do_Not_Break()
    {
        var engine = CreateEngine();
        var player = VeteranRb();
        var proj = engine.Project(
            player,
            StableProduction(player),
            null,
            League(),
            matchup: MatchupContext.Unavailable(),
            gameEnvironment: GameEnvironmentContext.Unavailable());

        Assert.True(proj.ProjectedFantasyPoints >= 0);
        Assert.False(proj.InputsUsed.MatchupContext);
        Assert.False(proj.InputsUsed.GameEnvironment);
        Assert.False(proj.InputsUsed.IntelligenceProfile);
    }

    [Fact]
    public void Available_Matchup_Applies_Without_Fabrication_Path()
    {
        var engine = CreateEngine();
        var player = VeteranRb();
        var production = StableProduction(player);
        var tough = engine.Project(
            player,
            production,
            Intel(player.Id),
            League(),
            matchup: new MatchupContext
            {
                IsAvailable = true,
                OpponentTeam = "SF",
                OpponentDefenseStrength = 0.8m,
                OpponentPositionPerformance = -0.5m,
                Summary = "Tough RB defense"
            });
        var soft = engine.Project(
            player,
            production,
            Intel(player.Id),
            League(),
            matchup: new MatchupContext
            {
                IsAvailable = true,
                OpponentTeam = "CAR",
                OpponentDefenseStrength = -0.7m,
                OpponentPositionPerformance = 0.6m,
                Summary = "Soft RB defense"
            });

        Assert.True(soft.ProjectedFantasyPoints > tough.ProjectedFantasyPoints);
        Assert.True(soft.InputsUsed.MatchupContext);
    }

    [Fact]
    public void ComparePlayers_And_ProjectRoster_Apis_Work()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var players = provider.GetRequiredService<IPlayerService>();
        var projections = provider.GetRequiredService<IProjectionService>();

        Assert.Equal(ProjectionEngineVersions.V0_1, projections.EngineVersion);

        var left = players.GetAllPlayers().First(p => p.FullName == "Patrick Mahomes");
        var right = players.GetAllPlayers().First(p => p.FullName == "Ja'Marr Chase");
        var comparison = projections.ComparePlayers(left.Id, right.Id);
        Assert.NotNull(comparison);
        Assert.NotEmpty(comparison!.ComparisonNotes);

        var roster = projections.ProjectRoster([left.Id, right.Id]);
        Assert.Equal(2, roster.Count);
        Assert.All(roster, p => Assert.Equal(ProjectionEngineVersions.V0_1, p.ProjectionVersion));

        var single = projections.ProjectPlayer(left.Id);
        Assert.NotNull(single);
        Assert.Equal(left.Id, single!.PlayerId);
    }

    [Fact]
    public void Developer_Monitor_Exposes_Engine_Version_And_Volatility()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var projections = provider.GetRequiredService<IProjectionService>();
        var status = provider.GetRequiredService<IProjectionSyncStatus>();
        _ = projections.GetAllProjections();

        Assert.Contains("Projection Engine", status.ProjectionEngine, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProjectionEngineVersions.V0_1, status.Version);
        Assert.True(status.PlayersProjected > 0);
        Assert.True(status.AverageVolatility > 0);
        Assert.NotNull(status.LastProjectionRun);
        Assert.Null(status.ProjectionErrors);
    }

    [Fact]
    public void Floor_Median_Ceiling_Not_Fixed_Offset_Only()
    {
        var engine = CreateEngine();
        var stable = VeteranRb();
        var volatilePlayer = SamplePlayer(Position.WR, "Wide Boom", yearsPro: 2);

        var stableProj = engine.Project(stable, StableProduction(stable), Intel(stable.Id), League());
        var volatileProj = engine.Project(
            volatilePlayer,
            TdHeavyProduction(volatilePlayer),
            Intel(volatilePlayer.Id, risk: 50, health: 35, usage: 90),
            League(),
            statisticalContext: Ctx(volatilePlayer.Id, StatisticalTrendSignal.Volatile, games: 2));

        var stableDown = stableProj.Median - stableProj.Floor;
        var volatileDown = volatileProj.Median - volatileProj.Floor;
        Assert.NotEqual(stableDown, volatileDown);
        Assert.True(volatileDown >= stableDown || volatileProj.Volatility > stableProj.Volatility);
    }

    private static ProjectionEngine CreateEngine() =>
        new(Options.Create(new ProjectionRuleOptions()));

    private static ProjectionLeagueContext League(ScoringType scoring = ScoringType.Ppr) => new()
    {
        LeagueId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        LeagueName = "Friends League",
        ScoringType = scoring,
        CurrentWeek = 1,
        Season = 2026,
        NumberOfTeams = 12
    };

    private static Player VeteranRb() => SamplePlayer(Position.RB, "Stable Vet", yearsPro: 7);

    private static Player RookieWr() => SamplePlayer(Position.WR, "Limited Rookie", yearsPro: 0);

    private static Player SamplePlayer(Position position, string fullName, int yearsPro)
    {
        var parts = fullName.Split(' ', 2);
        return new Player
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            FirstName = parts[0],
            LastName = parts.Length > 1 ? parts[1] : parts[0],
            Position = position,
            Team = "TEST",
            Status = PlayerStatus.Active,
            YearsPro = yearsPro,
            Age = 22 + yearsPro
        };
    }

    private static PlayerProductionSnapshot StableProduction(Player player) => new()
    {
        PlayerId = player.Id,
        PlayerName = player.FullName,
        Position = player.Position,
        Season = 2024,
        Source = ProductionDataSource.StatsService,
        SourceDescription = "Real historical data: CompletedSeason 2024",
        GamesPlayed = 16,
        RushingAttempts = 280,
        RushingYards = 1200,
        RushingTouchdowns = 10,
        Targets = 60,
        Receptions = 45,
        ReceivingYards = 350,
        ReceivingTouchdowns = 2
    };

    private static PlayerProductionSnapshot LimitedRookieProduction(Player player) => new()
    {
        PlayerId = player.Id,
        PlayerName = player.FullName,
        Position = player.Position,
        Season = 2025,
        Source = ProductionDataSource.StatsService,
        SourceDescription = "Real current data: CurrentSeason 2025",
        GamesPlayed = 2,
        Targets = 8,
        Receptions = 5,
        ReceivingYards = 60,
        ReceivingTouchdowns = 0
    };

    private static PlayerProductionSnapshot TdHeavyProduction(Player player) => new()
    {
        PlayerId = player.Id,
        PlayerName = player.FullName,
        Position = player.Position,
        Season = 2024,
        Source = ProductionDataSource.AttributeFallback,
        SourceDescription = "Attribute fallback",
        GamesPlayed = 10,
        Targets = 40,
        Receptions = 20,
        ReceivingYards = 280,
        ReceivingTouchdowns = 9
    };

    private static PlayerIntelligenceProfile Intel(
        Guid playerId,
        int health = 50,
        int opportunity = 50,
        int usage = 50,
        int risk = 10,
        int confidence = 70,
        TrendDirection trend = TrendDirection.Flat) =>
        new()
        {
            PlayerId = playerId,
            OverallConfidence = confidence,
            OverallRisk = risk,
            OpportunityScore = opportunity,
            TrendDirection = trend,
            HealthScore = health,
            UsageScore = usage,
            NewsMomentum = 50,
            LastUpdated = DateTimeOffset.UtcNow,
            SupportingFacts = [],
            Headline = "Neutral",
            ChangeSignal = IntelligenceChangeSignal.Neutral
        };

    private static PlayerStatisticalContext Ctx(Guid id, StatisticalTrendSignal trend, int games) => new()
    {
        PlayerId = id,
        AsOf = DateTimeOffset.UtcNow,
        GameLogsAvailable = games,
        NflSeasonsAvailable = 3,
        Trend = trend,
        RecentProduction = Window(FootballLevel.Nfl, "Recent", games, rushAtt: 15, rushYd: 70, tgt: 4, rec: 3, recYd: 22)
    };

    private static StatisticalProductionWindow Window(
        FootballLevel level,
        string label,
        int games,
        int rushAtt = 0,
        int rushYd = 0,
        int rushTd = 0,
        int tgt = 0,
        int rec = 0,
        int recYd = 0,
        int recTd = 0) =>
        new()
        {
            Level = level,
            Label = label,
            Games = games,
            Totals = new CanonicalCountingStats
            {
                RushAttempts = rushAtt * games,
                RushYards = rushYd * games,
                RushTouchdowns = rushTd * games,
                Targets = tgt * games,
                Receptions = rec * games,
                ReceivingYards = recYd * games,
                ReceivingTouchdowns = recTd * games
            },
            PerGame = new CanonicalCountingStats
            {
                RushAttempts = rushAtt,
                RushYards = rushYd,
                RushTouchdowns = rushTd,
                Targets = tgt,
                Receptions = rec,
                ReceivingYards = recYd,
                ReceivingTouchdowns = recTd
            }
        };
}
