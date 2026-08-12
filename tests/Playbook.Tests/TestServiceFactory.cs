using Playbook.Application;
using Playbook.Application.News;
using Playbook.Application.Players.Data;
using Playbook.Application.Stats;
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
        PlayerStatsProviderKind statsProvider = PlayerStatsProviderKind.Mock,
        string? sleeperBaseUrl = null,
        string? espnBaseUrl = null,
        bool backgroundRefreshEnabled = false,
        string propLinesProvider = "Mock",
        string? oddsApiKey = "",
        string? oddsApiBaseUrl = null,
        bool allowMockFallback = true)
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
            ["PlayerStats:Provider"] = statsProvider.ToString(),
            ["PlayerStats:HistoricalProvider"] = statsProvider == PlayerStatsProviderKind.Mock ? "Mock" : "Nflverse",
            ["PlayerStats:HistoricalSeasonCount"] = "3",
            ["PlayerStats:GameLogSeasonCount"] = "2",
            ["PlayerStats:CacheTtlMinutes"] = "360",
            ["PlayerStats:CacheFileName"] = $"player-stats-cache-tests-{Guid.NewGuid():N}.json",
            ["PlayerStats:GameLogCacheFileName"] = $"player-game-logs-cache-tests-{Guid.NewGuid():N}.json",
            ["CollegeStats:Provider"] = "Mock",
            ["CollegeStats:CacheTtlMinutes"] = "360",
            ["CollegeStats:CacheFileName"] = $"college-stats-cache-tests-{Guid.NewGuid():N}.json",
            ["CollegeStats:MaxAthletesPerSync"] = "50",
            ["Injuries:Provider"] = "Mock",
            ["Injuries:CacheTtlMinutes"] = "360",
            ["Injuries:CacheFileName"] = $"player-injuries-cache-tests-{Guid.NewGuid():N}.json",
            ["BackgroundRefresh:Enabled"] = backgroundRefreshEnabled ? "true" : "false",
            ["BackgroundRefresh:IntervalMinutes"] = "15",
            ["PropLines:Provider"] = propLinesProvider,
            ["PropLines:StaleAfterMinutes"] = "180",
            ["PropLines:FallbackToMockWhenEmpty"] = "true",
            ["PropLines:AllowMockFallback"] = allowMockFallback ? "true" : "false",
            ["PropLines:OddsApi:ApiKey"] = oddsApiKey ?? "",
            ["PropLines:OddsApi:BaseUrl"] = oddsApiBaseUrl ?? "https://api.the-odds-api.com/v4/",
            ["PropLines:OddsApi:TimeoutSeconds"] = "10"
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
