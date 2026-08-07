using Playbook.Application.Leagues;
using Playbook.Infrastructure.Leagues;
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
        return services;
    }
}
