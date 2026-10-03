using Playbook.Application.Players.Data;
using Playbook.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_And_AddInfrastructure_Register_Without_Throwing()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        Assert.NotNull(provider);
        Assert.NotNull(provider.GetRequiredService<IPlayerDataSyncStatus>());
    }

    [Fact]
    public void No_Shared_Singleton_Captures_Per_User_Scoped_State()
    {
        // ValidateScopes + ValidateOnBuild fail construction if any singleton (Quick Picks, data
        // services, background refresh...) depends on league/roster/personal state that must be
        // per customer.
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock, validateScopes: true);
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var leagueA = scopeA.ServiceProvider.GetRequiredService<Playbook.Application.Leagues.ILeagueState>();
        var leagueB = scopeB.ServiceProvider.GetRequiredService<Playbook.Application.Leagues.ILeagueState>();
        Assert.NotSame(leagueA, leagueB);

        var overlayA = scopeA.ServiceProvider.GetRequiredService<Playbook.Application.Players.IPlayerOverlayState>();
        var overlayB = scopeB.ServiceProvider.GetRequiredService<Playbook.Application.Players.IPlayerOverlayState>();
        Assert.NotSame(overlayA, overlayB);

        // Shared football data stays shared.
        Assert.Same(
            scopeA.ServiceProvider.GetRequiredService<Playbook.Application.Players.IPlayerService>(),
            scopeB.ServiceProvider.GetRequiredService<Playbook.Application.Players.IPlayerService>());
    }
}
