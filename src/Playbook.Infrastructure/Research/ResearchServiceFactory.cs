using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playbook.Application;
using Playbook.Application.News;
using Playbook.Application.Players.Data;
using Playbook.Application.Stats;

namespace Playbook.Infrastructure.Research;

/// <summary>
/// Offline DI host for research CLI / workbench. Mirrors TestServiceFactory defaults
/// without touching live Web production configuration.
/// </summary>
public static class ResearchServiceFactory
{
    public static ServiceProvider CreateProvider(bool backgroundRefreshEnabled = false)
    {
        var values = new Dictionary<string, string?>
        {
            ["PlayerData:Provider"] = PlayerDataProviderKind.Mock.ToString(),
            ["PlayerData:Sleeper:BaseUrl"] = "https://api.sleeper.app/v1/",
            ["PlayerData:Sleeper:TimeoutSeconds"] = "15",
            ["News:Provider"] = NewsProviderKind.Mock.ToString(),
            ["News:Espn:BaseUrl"] = "https://site.api.espn.com/apis/site/v2/",
            ["News:Espn:Limit"] = "20",
            ["News:Espn:TimeoutSeconds"] = "15",
            ["PlayerStats:Provider"] = PlayerStatsProviderKind.Mock.ToString(),
            ["PlayerStats:HistoricalProvider"] = "Mock",
            ["PlayerStats:HistoricalSeasonCount"] = "3",
            ["PlayerStats:GameLogSeasonCount"] = "2",
            ["PlayerStats:CacheTtlMinutes"] = "360",
            ["PlayerStats:CacheFileName"] = $"player-stats-cache-research-{Guid.NewGuid():N}.json",
            ["PlayerStats:GameLogCacheFileName"] = $"player-game-logs-cache-research-{Guid.NewGuid():N}.json",
            ["CollegeStats:Provider"] = "Mock",
            ["CollegeStats:CacheTtlMinutes"] = "360",
            ["CollegeStats:CacheFileName"] = $"college-stats-cache-research-{Guid.NewGuid():N}.json",
            ["CollegeStats:MaxAthletesPerSync"] = "50",
            ["Injuries:Provider"] = "Mock",
            ["Injuries:CacheTtlMinutes"] = "360",
            ["Injuries:CacheFileName"] = $"player-injuries-cache-research-{Guid.NewGuid():N}.json",
            ["BackgroundRefresh:Enabled"] = backgroundRefreshEnabled ? "true" : "false",
            ["BackgroundRefresh:IntervalMinutes"] = "15",
            ["PropLines:Provider"] = "Mock",
            ["PropLines:StaleAfterMinutes"] = "180",
            ["PropLines:FallbackToMockWhenEmpty"] = "true",
            ["PropLines:OddsApi:ApiKey"] = "",
            ["PropLines:OddsApi:BaseUrl"] = "https://api.the-odds-api.com/v4/",
            ["PropLines:OddsApi:TimeoutSeconds"] = "10"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration).AddApplication();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
