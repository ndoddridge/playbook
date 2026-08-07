using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Recommendations;
using Playbook.Infrastructure.Leagues;
using Playbook.Infrastructure.Players;
using Playbook.Infrastructure.Recommendations;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Infrastructure;

/// <summary>
/// Registers infrastructure implementations (persistence, external clients, adapters).
/// Database and external integrations are intentionally stubbed at this stage.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ILeagueService, MockLeagueService>();
        services.AddSingleton<IRecommendationService, MockRecommendationService>();
        services.AddSingleton<IPlayerService, MockPlayerService>();
        services.AddSingleton<IPlayerContextService, MockPlayerContextService>();
        return services;
    }
}
