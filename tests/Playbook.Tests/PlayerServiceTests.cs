using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Core.Players;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class PlayerServiceTests
{
    [Fact]
    public void Mock_Catalog_Has_Varied_Positions()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var service = provider.GetRequiredService<IPlayerService>();
        var players = service.GetAllPlayers();

        Assert.True(players.Count >= 15);
        Assert.Contains(players, p => p.Position == Position.QB);
        Assert.Contains(players, p => p.Position == Position.RB);
        Assert.Contains(players, p => p.Position == Position.WR);
        Assert.Contains(players, p => p.Position == Position.TE);
        Assert.Contains(players, p => p.Position == Position.K);
        Assert.Contains(players, p => p.Position == Position.DST);
    }

    [Fact]
    public void Search_And_Profile_Work()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var service = provider.GetRequiredService<IPlayerService>();
        var matches = service.SearchPlayers("Kelce");
        Assert.Single(matches);

        var profile = service.GetPlayerProfile(matches[0].Id);
        Assert.NotNull(profile);
        Assert.Equal("Travis Kelce", profile!.Player.FullName);
        Assert.NotNull(profile.SeasonStats);
        Assert.NotNull(profile.Trend);
    }

    [Fact]
    public void Live_Provider_Loads_Players_From_Sleeper()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Live);
        var service = provider.GetRequiredService<IPlayerService>();
        var status = provider.GetRequiredService<IPlayerDataSyncStatus>();

        var players = service.GetAllPlayers();

        Assert.True(players.Count > 50);
        Assert.Equal("Live", status.ConfiguredProvider);
        Assert.Equal("Live", status.ActiveProvider);
        Assert.False(status.UsedFallback);
        Assert.True(status.PlayersLoaded > 50);
        Assert.NotNull(status.LastSuccessfulSync);
        Assert.Null(status.LastError);
        Assert.Contains(players, p => p.Position == Position.QB);
        Assert.Contains(players, p => p.Position == Position.DST);
    }

    [Fact]
    public void Live_With_Bad_Url_Falls_Back_To_Mock()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Live,
            sleeperBaseUrl: "http://127.0.0.1:1/");
        var service = provider.GetRequiredService<IPlayerService>();
        var status = provider.GetRequiredService<IPlayerDataSyncStatus>();

        var players = service.GetAllPlayers();

        Assert.True(players.Count >= 15);
        Assert.Equal("Live", status.ConfiguredProvider);
        Assert.Equal("Mock", status.ActiveProvider);
        Assert.True(status.UsedFallback);
        Assert.False(string.IsNullOrWhiteSpace(status.LastError));
        Assert.Contains(players, p => p.FullName.Contains("Kelce", StringComparison.OrdinalIgnoreCase));
    }
}
