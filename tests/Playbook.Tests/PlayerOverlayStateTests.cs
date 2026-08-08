using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Core.Leagues;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class PlayerOverlayStateTests
{
    [Fact]
    public void Open_Close_And_League_Switch_Refresh_Context()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var overlay = provider.GetRequiredService<IPlayerOverlayState>();
        var leagues = provider.GetRequiredService<ILeagueState>();
        var playerId = Guid.Parse("11111111-1111-1111-1111-111111111101");

        overlay.Open(playerId);
        Assert.True(overlay.IsOpen);
        Assert.NotNull(overlay.Context);
        var first = overlay.Context!;
        var firstLeague = first.League?.Name;
        var firstSummary = first.RecommendationSummary;

        var dynasty = leagues.GetAllLeagues().Single(l => l.Name == "Dynasty League");
        leagues.SelectLeague(dynasty.Id);

        Assert.True(overlay.IsOpen);
        Assert.Equal(playerId, overlay.SelectedPlayerId);
        Assert.Equal("Dynasty League", overlay.Context?.League?.Name);
        Assert.Equal(ScoringType.HalfPpr, overlay.Context?.ScoringType);
        Assert.NotEqual(firstLeague, overlay.Context?.League?.Name);
        // League/scoring switch refreshes context for the same player.
        Assert.NotEqual(firstSummary, overlay.Context?.RecommendationSummary);
        Assert.Contains("Half PPR", overlay.Context?.RecommendationSummary ?? string.Empty);

        overlay.Close();
        Assert.False(overlay.IsOpen);
    }
}
