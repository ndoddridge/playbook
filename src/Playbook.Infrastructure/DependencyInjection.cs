using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Application.Recommendations;
using Playbook.Infrastructure.Intelligence.Services;
using Playbook.Infrastructure.Leagues;
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
        }
        else
        {
            services.Configure<PlayerDataOptions>(_ => { });
        }

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
        services.AddSingleton<IPlayerContextService, MockPlayerContextService>();
        services.AddSingleton<ILeagueService, MockLeagueService>();
        services.AddSingleton<IRecommendationService, MockRecommendationService>();
        services.AddSingleton<IIntelligenceService, MockIntelligenceService>();
        return services;
    }
}
