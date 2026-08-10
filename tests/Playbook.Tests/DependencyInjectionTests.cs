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
}
