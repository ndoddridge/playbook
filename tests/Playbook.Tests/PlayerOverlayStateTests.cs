using Playbook.Application;
using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Core.Leagues;
using Playbook.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class PlayerOverlayStateTests
{
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure().AddApplication();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Open_Close_And_League_Switch_Refresh_Context()
    {
        using var provider = CreateProvider();
        var overlay = provider.GetRequiredService<IPlayerOverlayState>();
        var leagues = provider.GetRequiredService<ILeagueState>();
        var playerId = Guid.Parse("11111111-1111-1111-1111-111111111101");

        overlay.Open(playerId);
        Assert.True(overlay.IsOpen);
        Assert.NotNull(overlay.Context);
        var firstProjection = overlay.Context!.WeeklyProjection;
        var firstLeague = overlay.Context.League?.Name;

        var dynasty = leagues.GetAllLeagues().Single(l => l.Name == "Dynasty League");
        leagues.SelectLeague(dynasty.Id);

        Assert.True(overlay.IsOpen);
        Assert.Equal(playerId, overlay.SelectedPlayerId);
        Assert.Equal("Dynasty League", overlay.Context?.League?.Name);
        Assert.Equal(ScoringType.HalfPpr, overlay.Context?.ScoringType);
        Assert.NotEqual(firstLeague, overlay.Context?.League?.Name);
        // Scoring change should alter mock fantasy values while keeping the same player.
        Assert.NotEqual(firstProjection, overlay.Context?.WeeklyProjection);

        overlay.Close();
        Assert.False(overlay.IsOpen);
    }
}
