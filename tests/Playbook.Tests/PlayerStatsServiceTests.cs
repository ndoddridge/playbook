using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;
using Playbook.Infrastructure.Stats;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class PlayerStatsServiceTests
{
    [Fact]
    public void Multiple_Seasons_Exist_For_One_Player()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var stats = provider.GetRequiredService<IPlayerStatsService>();
        var mahomesId = Guid.Parse("11111111-1111-1111-1111-111111111103");

        var seasons = stats.GetAvailableSeasons(mahomesId);
        Assert.True(seasons.Count >= 2, $"Expected multiple seasons, got {seasons.Count}");
        Assert.Contains(2024, seasons);
        Assert.Contains(2023, seasons);
    }

    [Fact]
    public void Player_With_Multiple_Nfl_Seasons_Receives_Historical_Records()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var stats = provider.GetRequiredService<IPlayerStatsService>();
        var chaseId = Guid.Parse("11111111-1111-1111-1111-111111111109");

        var historical = stats.GetStatsForPlayer(chaseId)
            .Where(r => r.Period == StatsPeriod.CompletedSeason)
            .ToList();

        Assert.True(historical.Count >= 2);
        Assert.All(historical, r => Assert.True(r.HasAnyCountingStat));
        Assert.All(historical, r => Assert.NotEqual(StatsPeriod.CurrentSeason, r.Period));
    }

    [Fact]
    public void Rookie_Can_Have_College_Statistics()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var stats = provider.GetRequiredService<IPlayerStatsService>();
        var danielsId = Guid.Parse("11111111-1111-1111-1111-111111111101");

        var college = stats.GetStatsForPlayer(danielsId)
            .Where(r => r.Period == StatsPeriod.College)
            .ToList();

        Assert.NotEmpty(college);
        Assert.Equal("LSU", college[0].CollegeSchool);
        Assert.True((college[0].PassYards ?? 0) > 0);
    }

    [Fact]
    public async Task Current_Season_Is_Distinguishable_From_Completed()
    {
        var mock = new MockPlayerStatsProvider();
        var rows = await mock.GetSeasonStatsAsync(new PlayerStatsSyncRequest
        {
            CurrentSeason = 2025,
            CompletedSeasons = [2024, 2023],
            SeasonType = "regular"
        });

        Assert.Contains(rows, r => r.Period == StatsPeriod.CurrentSeason && r.Season == 2025);
        Assert.Contains(rows, r => r.Period == StatsPeriod.CompletedSeason && r.Season == 2024);
        Assert.DoesNotContain(rows, r => r.Period == StatsPeriod.CurrentSeason && r.Season == 2024);
    }

    [Fact]
    public void Missing_Statistics_Are_Not_Fabricated_For_Unknown_Players()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var stats = provider.GetRequiredService<IPlayerStatsService>();
        var unknown = Guid.Parse("99999999-9999-9999-9999-999999999999");

        Assert.Empty(stats.GetStatsForPlayer(unknown));
        Assert.Null(stats.GetPrimaryProductionSeason(unknown));
    }

    [Fact]
    public void Projection_Engine_Consumes_Stats_Service_Production()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var players = provider.GetRequiredService<Playbook.Application.Players.IPlayerService>();
        var production = provider.GetRequiredService<IPlayerProductionProvider>();
        var status = provider.GetRequiredService<IPlayerStatsSyncStatus>();

        var mahomes = players.GetAllPlayers().First(p => p.FullName == "Patrick Mahomes");
        var snapshot = production.GetProduction(mahomes);

        Assert.Equal(ProductionDataSource.StatsService, snapshot.Source);
        Assert.Contains("Stats service", snapshot.SourceDescription, StringComparison.OrdinalIgnoreCase);
        Assert.True(snapshot.PassingYards > 0);
        Assert.True(status.PlayersWithStats > 0);
        Assert.True(status.HistoricalRecords > 0);
    }
}
