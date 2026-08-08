using Playbook.Core.Stats.Models;

namespace Playbook.Application.Stats.Interfaces;

/// <summary>
/// Statistical interface consumed by the Intelligence Engine.
/// Reasons over normalized Playbook data — not provider-specific objects.
/// </summary>
public interface IPlayerStatisticalContextService
{
    PlayerStatisticalContext? GetContext(Guid playerId);

    IReadOnlyList<PlayerStatisticalContext> GetAllContexts();
}
