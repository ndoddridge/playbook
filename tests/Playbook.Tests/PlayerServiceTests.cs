using Playbook.Application;
using Playbook.Application.Players;
using Playbook.Core.Players;
using Playbook.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class PlayerServiceTests
{
    private static IPlayerService CreateService()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure().AddApplication();
        return services.BuildServiceProvider().GetRequiredService<IPlayerService>();
    }

    [Fact]
    public void Mock_Catalog_Has_Varied_Positions()
    {
        var service = CreateService();
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
        var service = CreateService();
        var matches = service.SearchPlayers("Kelce");
        Assert.Single(matches);

        var profile = service.GetPlayerProfile(matches[0].Id);
        Assert.NotNull(profile);
        Assert.Equal("Travis Kelce", profile!.Player.FullName);
        Assert.NotNull(profile.SeasonStats);
        Assert.NotNull(profile.Trend);
    }
}
