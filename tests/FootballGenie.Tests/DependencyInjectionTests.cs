using FootballGenie.Application;
using FootballGenie.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FootballGenie.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_And_AddInfrastructure_Register_Without_Throwing()
    {
        var services = new ServiceCollection();

        services.AddApplication().AddInfrastructure();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider);
    }
}
