using Playbook.Application;
using Playbook.Application.Leagues;
using Playbook.Infrastructure;
using Playbook.Infrastructure.Leagues;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class LeagueStateTests
{
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure().AddApplication();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [Fact]
    public void Default_Current_League_Is_Friends_League()
    {
        using var provider = CreateProvider();
        var state = provider.GetRequiredService<ILeagueState>();

        Assert.Equal("Friends League", state.GetCurrentLeague()?.Name);
        Assert.Equal(3, state.GetAllLeagues().Count);
    }

    [Fact]
    public void SelectLeague_Updates_Current_And_Raises_Changed()
    {
        using var provider = CreateProvider();
        var state = provider.GetRequiredService<ILeagueState>();
        var dynasty = state.GetAllLeagues().Single(l => l.Name == "Dynasty League");
        var changed = 0;
        state.Changed += () => changed++;

        state.SelectLeague(dynasty.Id);

        Assert.Equal("Dynasty League", state.CurrentLeague?.Name);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void MockLeagueService_SelectLeague_Is_Reflected_By_GetCurrentLeague()
    {
        var service = new MockLeagueService();
        var work = service.GetAllLeagues().Single(l => l.Name == "Work League");

        service.SelectLeague(work.Id);

        Assert.Equal("Work League", service.GetCurrentLeague()?.Name);
    }
}
