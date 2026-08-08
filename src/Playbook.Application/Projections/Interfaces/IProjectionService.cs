using Playbook.Core.Projections.Models;

namespace Playbook.Application.Projections.Interfaces;

/// <summary>
/// Facade over the Projection Engine for UI and downstream engines.
/// </summary>
public interface IProjectionService
{
    PlayerProjection? GetProjection(Guid playerId);

    IReadOnlyList<PlayerProjection> GetAllProjections();

    IReadOnlyList<PlayerProjection> GetTopProjections(int count = 8);

    void Refresh();
}