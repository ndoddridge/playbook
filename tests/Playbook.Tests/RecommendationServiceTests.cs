using Playbook.Application.Players.Data;
using Playbook.Application.Recommendations;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class RecommendationServiceTests
{
    [Fact]
    public void Mock_Service_Returns_Top_Recommendations()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var service = provider.GetRequiredService<IRecommendationService>();
        var top = service.GetTopRecommendations(5);

        Assert.Equal(5, top.Count);
        Assert.Contains(top, r => r.Title == "Start Jayden Daniels");
        Assert.All(top, r => Assert.NotEqual(Guid.Empty, r.Id));
    }
}
