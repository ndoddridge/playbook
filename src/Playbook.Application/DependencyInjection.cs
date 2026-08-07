using Playbook.Application.Leagues;
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
        return services;
    }
}
