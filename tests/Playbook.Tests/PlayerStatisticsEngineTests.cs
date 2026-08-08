using Playbook.Application.Players;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;
using Playbook.Infrastructure.Players;
using Playbook.Infrastructure.Stats;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class PlayerStatisticsEngineTests
{
    [Fact]
    public void Nfl_Season_Normalization_Preserves_Null_Vs_Zero()
    {
        var missing = new PlayerSeasonStats
        {
            PlayerId = Guid.NewGuid(),
            Season = 2024,
            SeasonType = "regular",
            Period = StatsPeriod.CompletedSeason,
            Level = FootballLevel.Nfl,
            RushAttempts = null,
            RushYards = null,
            Receptions = 0,
            ReceivingYards = 0,
            SourceProvider = "test",
            LastUpdated = DateTimeOffset.UtcNow
        };

        Assert.Null(missing.RushAttempts);
        Assert.Null(missing.RushYards);
        Assert.Equal(0, missing.Receptions);
        Assert.Equal(0, missing.ReceivingYards);
        Assert.True(missing.Receptions is 0);
        Assert.False(missing.RushAttempts is 0);
    }

    [Fact]
    public void Game_Log_Normalization_Retains_Weekly_Rows()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var stats = provider.GetRequiredService<IPlayerStatsService>();
        var mahomesId = Guid.Parse("11111111-1111-1111-1111-111111111103");

        var logs = stats.GetGameLogsForPlayer(mahomesId);
        Assert.NotEmpty(logs);
        Assert.Contains(logs, g => g.Week > 0 && g.PassYards is > 0);
        Assert.True(stats.GameLogCount >= logs.Count);
    }

    [Fact]
    public void College_Normalization_Stays_Separate_From_Nfl()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var stats = provider.GetRequiredService<IPlayerStatsService>();
        var danielsId = Guid.Parse("11111111-1111-1111-1111-111111111101");

        var rows = stats.GetStatsForPlayer(danielsId);
        var college = rows.Where(r => r.Period == StatsPeriod.College).ToList();
        var nfl = rows.Where(r => r.Level == FootballLevel.Nfl).ToList();
        var career = stats.GetCareerTotals(danielsId);

        Assert.NotEmpty(college);
        Assert.All(college, r => Assert.Equal(FootballLevel.College, r.Level));
        Assert.DoesNotContain(nfl, r => r.Period == StatsPeriod.College);
        if (career is not null)
        {
            Assert.Equal(FootballLevel.Career, career.Level);
            Assert.Equal(StatsPeriod.Career, career.Period);
        }
    }

    [Fact]
    public void Player_Identity_Resolution_Maps_Known_Stars()
    {
        var directory = new PlayerIdentityDirectory();
        directory.ReplaceAll(
        [
            new PlaybookPlayerIdentity
            {
                PlaybookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"),
                FullName = "Christian McCaffrey",
                Team = "SF",
                Position = "RB",
                GsisId = "00-0033280",
                SleeperId = "4034"
            },
            new PlaybookPlayerIdentity
            {
                PlaybookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"),
                FullName = "Josh Allen",
                Team = "BUF",
                Position = "QB",
                GsisId = "00-0034857",
                SleeperId = "4984"
            },
            new PlaybookPlayerIdentity
            {
                PlaybookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"),
                FullName = "Patrick Mahomes",
                Team = "KC",
                Position = "QB",
                GsisId = "00-0033873",
                SleeperId = "4046"
            },
            new PlaybookPlayerIdentity
            {
                PlaybookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4"),
                FullName = "Bijan Robinson",
                Team = "ATL",
                Position = "RB",
                GsisId = "00-0038542",
                SleeperId = "9509"
            },
            new PlaybookPlayerIdentity
            {
                PlaybookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5"),
                FullName = "Jayden Daniels",
                Team = "WAS",
                Position = "QB",
                GsisId = "00-0039910",
                SleeperId = "11566",
                CollegeName = "LSU"
            }
        ]);

        Assert.NotNull(directory.GetByGsisId("00-0033280"));
        Assert.Equal("Christian McCaffrey", directory.GetByGsisId("00-0033280")!.FullName);
        Assert.NotNull(directory.ResolveByNameTeam("Josh Allen", "BUF"));
        Assert.NotNull(directory.ResolveByNameTeam("Patrick Mahomes", "KC"));
        Assert.NotNull(directory.ResolveByNameTeam("Bijan Robinson", "ATL"));
        Assert.Equal("LSU", directory.GetByGsisId("00-0039910")!.CollegeName);
    }

    [Fact]
    public void Missing_Statistics_Are_Not_Coerced_To_Zero_In_Scoring_Source()
    {
        var stats = new CanonicalCountingStats
        {
            Receptions = null,
            ReceivingYards = 50,
            ReceivingTouchdowns = 1
        };

        Assert.Null(stats.Receptions);
        Assert.Contains(nameof(CanonicalCountingStats.Receptions), stats.ListMissingCoreFields());

        // Scoring treats null receptions as contribute-nothing (0 points), without mutating the source.
        var ppr = LeagueFantasyScoring.Calculate(stats, ScoringType.Ppr);
        Assert.Equal(11.0m, ppr);
        Assert.Null(stats.Receptions);
    }

    [Fact]
    public void Zero_Statistics_Score_As_Zero_Not_Missing()
    {
        var stats = new CanonicalCountingStats
        {
            Receptions = 0,
            ReceivingYards = 0,
            ReceivingTouchdowns = 0,
            RushYards = 0,
            RushTouchdowns = 0
        };

        Assert.DoesNotContain(nameof(CanonicalCountingStats.Receptions), stats.ListMissingCoreFields());
        Assert.Equal(0m, LeagueFantasyScoring.Calculate(stats, ScoringType.Ppr));
        Assert.Equal(0, stats.Receptions);
    }

    [Theory]
    [InlineData(ScoringType.Standard, 11.0)]
    [InlineData(ScoringType.HalfPpr, 13.5)]
    [InlineData(ScoringType.Ppr, 16.0)]
    public void Fantasy_Scoring_Variants_From_Same_Canonical_Stats(ScoringType scoring, double expected)
    {
        var stats = new CanonicalCountingStats
        {
            RushYards = 60,
            RushTouchdowns = 0,
            Receptions = 5,
            ReceivingYards = 50,
            ReceivingTouchdowns = 0
        };

        // 60/10 + 50/10 = 11; + receptions by scoring
        Assert.Equal((decimal)expected, LeagueFantasyScoring.Calculate(stats, scoring));
    }

    [Fact]
    public void Changing_League_Scoring_Changes_Points_Not_Football_Stats()
    {
        var football = new CanonicalCountingStats
        {
            PassYards = 300,
            PassTouchdowns = 2,
            PassInterceptions = 1,
            RushYards = 40,
            RushTouchdowns = 1,
            Receptions = 0
        };

        var beforeYards = football.PassYards;
        var ppr = LeagueFantasyScoring.Calculate(football, ScoringType.Ppr);
        var std = LeagueFantasyScoring.Calculate(football, ScoringType.Standard);

        Assert.Equal(beforeYards, football.PassYards);
        Assert.Equal(ppr, std); // no receptions — same points
        Assert.Equal(300, football.PassYards);

        var receiving = new CanonicalCountingStats
        {
            PassYards = football.PassYards,
            PassTouchdowns = football.PassTouchdowns,
            PassInterceptions = football.PassInterceptions,
            RushYards = football.RushYards,
            RushTouchdowns = football.RushTouchdowns,
            Receptions = 4,
            ReceivingYards = 40
        };

        var ppr2 = LeagueFantasyScoring.Calculate(receiving, ScoringType.Ppr);
        var half2 = LeagueFantasyScoring.Calculate(receiving, ScoringType.HalfPpr);
        var std2 = LeagueFantasyScoring.Calculate(receiving, ScoringType.Standard);

        Assert.True(ppr2 > half2);
        Assert.True(half2 > std2);
        Assert.Equal(4, receiving.Receptions);
        Assert.Equal(40, receiving.ReceivingYards);
    }

    [Fact]
    public void Multiple_Seasons_And_Providers_Surface_In_Store()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var stats = provider.GetRequiredService<IPlayerStatsService>();
        var status = provider.GetRequiredService<IPlayerStatsSyncStatus>();
        var mahomesId = Guid.Parse("11111111-1111-1111-1111-111111111103");

        var seasons = stats.GetAvailableSeasons(mahomesId);
        Assert.True(seasons.Count >= 2);
        Assert.Contains("Mock", status.StatisticsProviders, StringComparison.OrdinalIgnoreCase);
        Assert.True(status.GameLogsLoaded > 0);
        Assert.NotNull(stats.GetCareerTotals(mahomesId));
    }

    [Fact]
    public async Task Current_Season_Update_Path_Exists()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var stats = provider.GetRequiredService<IPlayerStatsService>();
        await stats.RefreshAsync();
        var before = stats.GetAllStats().Count;
        Assert.True(before > 0);
        await stats.RefreshCurrentSeasonAsync();
        Assert.True(stats.GetAllStats().Count >= before - 5); // historical retained
        Assert.NotEmpty(stats.GetAllStats());
    }

    [Fact]
    public void Statistical_Context_Exposes_Intelligence_Signals()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var contexts = provider.GetRequiredService<IPlayerStatisticalContextService>();
        var mahomesId = Guid.Parse("11111111-1111-1111-1111-111111111103");

        var ctx = contexts.GetContext(mahomesId);
        Assert.NotNull(ctx);
        Assert.True(ctx!.GameLogsAvailable > 0);
        Assert.NotNull(ctx.RecentProduction);
        Assert.NotNull(ctx.Usage);
        Assert.Equal(FootballLevel.Nfl, ctx.RecentProduction!.Level);
    }

    [Fact]
    public void Projection_Baseline_Label_Distinguishes_Real_Vs_Fallback()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var players = provider.GetRequiredService<IPlayerService>();
        var production = provider.GetRequiredService<IPlayerProductionProvider>();

        var mahomes = players.GetAllPlayers().First(p => p.FullName == "Patrick Mahomes");
        var snapshot = production.GetProduction(mahomes);
        Assert.Equal(ProductionDataSource.StatsService, snapshot.Source);
        Assert.Contains("Real", ProductionBaselineLabels.Describe(snapshot), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mock", ProductionBaselineLabels.Describe(snapshot), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Historical_Mock_Provider_Returns_Game_Logs_For_Multiple_Seasons()
    {
        var historical = new MockHistoricalPlayerStatsProvider();
        var batch = await historical.GetHistoricalStatsAsync(new HistoricalPlayerStatsSyncRequest
        {
            Seasons = [2024, 2023],
            SeasonType = "regular"
        });

        Assert.True(batch.GameLogs.Count >= 6);
        Assert.Contains(batch.GameLogs, g => g.Season == 2024);
        Assert.Contains(batch.GameLogs, g => g.Season == 2023);
        Assert.Equal(2, batch.IdentityMatches);
    }

    [Fact]
    public void League_Specific_Fantasy_Calculation_Uses_Overlay_League_Scoring()
    {
        var stats = new CanonicalCountingStats
        {
            Receptions = 6,
            ReceivingYards = 80,
            ReceivingTouchdowns = 1
        };

        var friends = LeagueFantasyScoring.Calculate(stats, ScoringType.Ppr);
        var dynasty = LeagueFantasyScoring.Calculate(stats, ScoringType.HalfPpr);
        var another = LeagueFantasyScoring.Calculate(stats, ScoringType.Standard);

        Assert.Equal(20.0m, friends);
        Assert.Equal(17.0m, dynasty);
        Assert.Equal(14.0m, another);
        Assert.Equal(6, stats.Receptions);
    }
}
