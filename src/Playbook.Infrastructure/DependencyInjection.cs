using Playbook.Application.Intelligence;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.News;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Application.Recommendations;
using Playbook.Infrastructure.Hosting;
using Playbook.Infrastructure.Intelligence.Services;
using Playbook.Infrastructure.Leagues;
using Playbook.Infrastructure.News;
using Playbook.Infrastructure.Players;
using Playbook.Infrastructure.Recommendations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Infrastructure;

/// <summary>
/// Registers infrastructure implementations (persistence, external clients, adapters).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<PlayerDataOptions>(configuration.GetSection(PlayerDataOptions.SectionName));
            services.Configure<NewsOptions>(configuration.GetSection(NewsOptions.SectionName));
            services.Configure<BackgroundRefreshOptions>(configuration.GetSection(BackgroundRefreshOptions.SectionName));
        }
        else
        {
            services.Configure<PlayerDataOptions>(_ => { });
            services.Configure<NewsOptions>(_ => { });
            services.Configure<BackgroundRefreshOptions>(_ => { });
        }

        RegisterPlayerData(services);
        RegisterNews(services);
        RegisterIntelligence(services);

        services.AddSingleton<IPlayerContextService, MockPlayerContextService>();
        services.AddSingleton<ILeagueService, MockLeagueService>();
        services.AddSingleton<IRecommendationService, MockRecommendationService>();

        services.AddHostedService<DataRefreshBackgroundService>();
        return services;
    }

    private static void RegisterIntelligence(IServiceCollection services)
    {
        services.AddSingleton<IntelligenceSyncStatus>();
        services.AddSingleton<IIntelligenceSyncStatus>(sp => sp.GetRequiredService<IntelligenceSyncStatus>());
        services.AddSingleton<IIntelligenceAnalyzer, IntelligenceAnalyzer>();
        services.AddSingleton<IIntelligenceService, IntelligenceService>();
    }

    private static void RegisterPlayerData(IServiceCollection services)
    {
        services.AddSingleton<PlayerDataSyncStatus>();
        services.AddSingleton<IPlayerDataSyncStatus>(sp => sp.GetRequiredService<PlayerDataSyncStatus>());

        services.AddSingleton<MockPlayerDataProvider>();
        services.AddSingleton<IPlayerDataProvider>(sp => sp.GetRequiredService<MockPlayerDataProvider>());

        services.AddHttpClient(LivePlayerDataProvider.HttpClientName, (sp, client) =>
        {
            var sleeper = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlayerDataOptions>>().Value.Sleeper;
            var baseUrl = string.IsNullOrWhiteSpace(sleeper.BaseUrl)
                ? "https://api.sleeper.app/v1/"
                : sleeper.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(sleeper.TimeoutSeconds, 5, 120));
            if (!string.IsNullOrWhiteSpace(sleeper.ApiKey))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", sleeper.ApiKey);
            }
        });

        services.AddSingleton<LivePlayerDataProvider>();
        services.AddSingleton<IPlayerDataProvider>(sp => sp.GetRequiredService<LivePlayerDataProvider>());
        services.AddSingleton<IPlayerService, PlayerService>();
    }

    private static void RegisterNews(IServiceCollection services)
    {
        services.AddSingleton<NewsSyncStatus>();
        services.AddSingleton<INewsSyncStatus>(sp => sp.GetRequiredService<NewsSyncStatus>());

        services.AddSingleton<MockNewsProvider>();
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<MockNewsProvider>());

        services.AddHttpClient(LiveNewsProvider.HttpClientName, (sp, client) =>
        {
            var espn = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewsOptions>>().Value.Espn;
            var baseUrl = string.IsNullOrWhiteSpace(espn.BaseUrl)
                ? "https://site.api.espn.com/apis/site/v2/"
                : espn.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(espn.TimeoutSeconds, 5, 120));
            if (!string.IsNullOrWhiteSpace(espn.ApiKey))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", espn.ApiKey);
            }

            // ESPN blocks unknown/custom agents; use a standard browser UA for public reads.
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; Playbook/0.1; +https://github.com/ndoddridge/playbook)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        });

        services.AddSingleton<LiveNewsProvider>();
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<LiveNewsProvider>());
        services.AddSingleton<INewsProvider, NewsProvider>();
    }
}
