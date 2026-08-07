using Microsoft.Extensions.DependencyInjection;

namespace FootballGenie.Application;

/// <summary>
/// Registers application-layer services (use cases, orchestrators, application ports).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Application services will be registered here as features are added.
        return services;
    }
}
