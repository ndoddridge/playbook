using Playbook.Application;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Playbook.Tests;

internal static class TestServiceFactory
{
    public static ServiceProvider CreateProvider(
        PlayerDataProviderKind provider = PlayerDataProviderKind.Mock,
        string? sleeperBaseUrl = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["PlayerData:Provider"] = provider.ToString(),
            ["PlayerData:Sleeper:BaseUrl"] = sleeperBaseUrl ?? "https://api.sleeper.app/v1/",
            ["PlayerData:Sleeper:TimeoutSeconds"] = "15"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration).AddApplication();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
