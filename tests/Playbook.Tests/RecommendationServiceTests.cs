using Playbook.Application.Leagues;
using Playbook.Application.Players.Data;
using Playbook.Application.Recommendations;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class RecommendationServiceTests
{
    [Fact]
    public void Mock_Service_Returns_Top_Recommendations_For_Current_League()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var service = provider.GetRequiredService<IRecommendationService>();
        var top = service.GetTopRecommendations(5);

        Assert.NotEmpty(top);
        Assert.All(top, r => Assert.NotEqual(Guid.Empty, r.Id));
        Assert.All(top, r => Assert.Equal(leagues.CurrentLeague!.Id, r.LeagueId));
        Assert.All(top, r => Assert.Equal(leagues.CurrentLeague!.SelectedRosterId, r.SelectedRosterId));
        Assert.All(top, r => Assert.Equal("Friends League", r.LeagueName));
    }
}
