using Playbook.Application;
using Playbook.Application.Recommendations;
using Playbook.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class RecommendationServiceTests
{
    [Fact]
    public void Mock_Service_Returns_Top_Recommendations()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure().AddApplication();
        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<IRecommendationService>();
        var top = service.GetTopRecommendations(5);

        Assert.Equal(5, top.Count);
        Assert.Contains(top, r => r.Title == "Start Jayden Daniels");
        Assert.All(top, r => Assert.NotEqual(Guid.Empty, r.Id));
    }
}
