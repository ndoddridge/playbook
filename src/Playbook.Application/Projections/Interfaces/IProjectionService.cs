using Playbook.Core.Projections.Models;

namespace Playbook.Application.Projections.Interfaces;

/// <summary>
/// Facade over the Projection Engine for UI and downstream Decision engines.
/// </summary>
public interface IProjectionService
{
    string EngineVersion { get; }

    PlayerProjection? GetProjection(Guid playerId);

    /// <summary>Project a single player (forces ensure/refresh of cache context).</summary>
    PlayerProjection? ProjectPlayer(Guid playerId);

    IReadOnlyList<PlayerProjection> GetAllProjections();

    IReadOnlyList<PlayerProjection> GetTopProjections(int count = 8);

    PlayerProjectionComparison? ComparePlayers(Guid leftPlayerId, Guid rightPlayerId);

    IReadOnlyList<PlayerProjection> ProjectRoster(IEnumerable<Guid> playerIds);

    void Refresh();
}
