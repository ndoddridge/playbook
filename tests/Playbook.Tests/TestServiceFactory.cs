using Playbook.Application;
using Playbook.Application.News;
using Playbook.Application.Players.Data;
using Playbook.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Playbook.Tests;

internal static class TestServiceFactory
{
    public static ServiceProvider CreateProvider(
        PlayerDataProviderKind playerProvider = PlayerDataProviderKind.Mock,
        NewsProviderKind newsProvider = NewsProviderKind.Mock,
        string? sleeperBaseUrl = null,
        string? espnBaseUrl = null,
        bool backgroundRefreshEnabled = false)
    {
        var values = new Dictionary<string, string?>
        {
            ["PlayerData:Provider"] = playerProvider.ToString(),
            ["PlayerData:Sleeper:BaseUrl"] = sleeperBaseUrl ?? "https://api.sleeper.app/v1/",
            ["PlayerData:Sleeper:TimeoutSeconds"] = "15",
            ["News:Provider"] = newsProvider.ToString(),
            ["News:Espn:BaseUrl"] = espnBaseUrl ?? "https://site.api.espn.com/apis/site/v2/",
            ["News:Espn:Limit"] = "20",
            ["News:Espn:TimeoutSeconds"] = "15",
            ["BackgroundRefresh:Enabled"] = backgroundRefreshEnabled ? "true" : "false",
            ["BackgroundRefresh:IntervalMinutes"] = "15"
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
