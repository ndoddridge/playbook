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
        // Infrastructure services (EF Core, PostgreSQL, external APIs) will be
        // registered here when those concerns are introduced.
        return services;
    }
}
