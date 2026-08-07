using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Application;

/// <summary>
/// Registers application-layer services (use cases, orchestrators, application ports).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ILeagueState, LeagueStateService>();
        services.AddSingleton<IPlayerOverlayState, PlayerOverlayState>();
        return services;
    }
}
