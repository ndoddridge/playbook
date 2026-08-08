using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.News;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Recommendations;
using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Infrastructure.Hosting;
using Playbook.Infrastructure.Injuries;
using Playbook.Infrastructure.Intelligence.Services;
using Playbook.Infrastructure.Leagues;
using Playbook.Infrastructure.News;
using Playbook.Infrastructure.Players;
using Playbook.Infrastructure.Projections.Services;
using Playbook.Infrastructure.Recommendations;
using Playbook.Infrastructure.Stats;
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
            services.Configure<IntelligenceScoringOptions>(configuration.GetSection(IntelligenceScoringOptions.SectionName));
            services.Configure<ProjectionRuleOptions>(configuration.GetSection(ProjectionRuleOptions.SectionName));
            services.Configure<PlayerStatsOptions>(configuration.GetSection(PlayerStatsOptions.SectionName));
            services.Configure<CollegeStatsOptions>(configuration.GetSection(CollegeStatsOptions.SectionName));
            services.Configure<InjuryOptions>(configuration.GetSection(InjuryOptions.SectionName));
        }
        else
        {
            services.Configure<PlayerDataOptions>(_ => { });
            services.Configure<NewsOptions>(_ => { });
            services.Configure<BackgroundRefreshOptions>(_ => { });
            services.Configure<IntelligenceScoringOptions>(_ => { });
            services.Configure<ProjectionRuleOptions>(_ => { });
            services.Configure<PlayerStatsOptions>(_ => { });
            services.Configure<CollegeStatsOptions>(_ => { });
            services.Configure<InjuryOptions>(_ => { });
        }

        RegisterPlayerData(services);
        RegisterPlayerStats(services);
        RegisterCollegeStats(services);
        RegisterInjuries(services);
        RegisterNews(services);
        RegisterIntelligence(services);
        RegisterProjections(services);

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
        services.AddSingleton<IIntelligenceAggregator, IntelligenceAggregator>();
        services.AddSingleton<IIntelligenceService, IntelligenceService>();
    }

    private static void RegisterProjections(IServiceCollection services)
    {
        services.AddSingleton<ProjectionSyncStatus>();
        services.AddSingleton<IProjectionSyncStatus>(sp => sp.GetRequiredService<ProjectionSyncStatus>());
        services.AddSingleton<IPlayerProductionProvider, PlayerProductionProvider>();
        services.AddSingleton<IProjectionEngine, ProjectionEngine>();
        services.AddSingleton<IProjectionService, ProjectionService>();
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

    private static void RegisterPlayerStats(IServiceCollection services)
    {
        services.AddSingleton<PlayerStatsSyncStatus>();
        services.AddSingleton<IPlayerStatsSyncStatus>(sp => sp.GetRequiredService<PlayerStatsSyncStatus>());
        services.AddSingleton<PlayerStatsCacheStore>();

        services.AddSingleton<MockPlayerStatsProvider>();
        services.AddSingleton<IPlayerStatsProvider>(sp => sp.GetRequiredService<MockPlayerStatsProvider>());

        services.AddHttpClient(LivePlayerStatsProvider.HttpClientName, (sp, client) =>
        {
            var sleeper = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlayerDataOptions>>().Value.Sleeper;
            var stats = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlayerStatsOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(sleeper.BaseUrl)
                ? "https://api.sleeper.app/v1/"
                : sleeper.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(stats.TimeoutSeconds, 10, 180));
            if (!string.IsNullOrWhiteSpace(sleeper.ApiKey))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", sleeper.ApiKey);
            }
        });

        services.AddSingleton<LivePlayerStatsProvider>();
        services.AddSingleton<IPlayerStatsProvider>(sp => sp.GetRequiredService<LivePlayerStatsProvider>());
        services.AddSingleton<IPlayerStatsService, PlayerStatsService>();
    }

    private static void RegisterCollegeStats(IServiceCollection services)
    {
        services.AddSingleton<CollegeStatsSyncStatus>();
        services.AddSingleton<ICollegeStatsSyncStatus>(sp => sp.GetRequiredService<CollegeStatsSyncStatus>());
        services.AddSingleton<CollegeStatsCacheStore>();

        services.AddSingleton<MockCollegeStatsProvider>();
        services.AddSingleton<ICollegeStatsProvider>(sp => sp.GetRequiredService<MockCollegeStatsProvider>());

        services.AddHttpClient(LiveCollegeStatsProvider.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CollegeStatsOptions>>().Value;
            client.BaseAddress = new Uri("https://site.web.api.espn.com/apis/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 15, 180));
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; Playbook/0.1; +https://github.com/ndoddridge/playbook)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        });

        services.AddSingleton<LiveCollegeStatsProvider>();
        services.AddSingleton<ICollegeStatsProvider>(sp => sp.GetRequiredService<LiveCollegeStatsProvider>());
        services.AddSingleton<CollegeStatsService>();
    }

    private static void RegisterInjuries(IServiceCollection services)
    {
        services.AddSingleton<InjurySyncStatus>();
        services.AddSingleton<IInjurySyncStatus>(sp => sp.GetRequiredService<InjurySyncStatus>());
        services.AddSingleton<InjuryCacheStore>();

        services.AddSingleton<MockPlayerInjuryProvider>();
        services.AddSingleton<IPlayerInjuryProvider>(sp => sp.GetRequiredService<MockPlayerInjuryProvider>());

        services.AddHttpClient(LivePlayerInjuryProvider.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InjuryOptions>>().Value;
            client.BaseAddress = new Uri("https://site.web.api.espn.com/apis/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 15, 180));
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; Playbook/0.1; +https://github.com/ndoddridge/playbook)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        });

        services.AddSingleton<LivePlayerInjuryProvider>();
        services.AddSingleton<IPlayerInjuryProvider>(sp => sp.GetRequiredService<LivePlayerInjuryProvider>());
        services.AddSingleton<IPlayerInjuryService, PlayerInjuryService>();
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
