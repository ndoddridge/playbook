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

    /// <summary>
    /// Project every player for an EXPLICIT league context rather than whichever league happens
    /// to be connected. Used by the Draft Assistant when following a draft (e.g. a Sleeper mock)
    /// whose scoring format differs from the ambient connected league — without that, PPR vs.
    /// Standard vs. Half-PPR would silently fail to affect player value for exactly the draft
    /// being followed. Does not read or write the cached ambient projections used by
    /// <see cref="GetAllProjections()"/>; this is a separate, uncached computation for exactly
    /// the context given.
    /// </summary>
    IReadOnlyList<PlayerProjection> GetAllProjections(ProjectionLeagueContext context);

    IReadOnlyList<PlayerProjection> GetTopProjections(int count = 8);

    PlayerProjectionComparison? ComparePlayers(Guid leftPlayerId, Guid rightPlayerId);

    IReadOnlyList<PlayerProjection> ProjectRoster(IEnumerable<Guid> playerIds);

    /// <summary>Rebuild projections for the current league scoring context immediately.</summary>
    void Refresh();

    /// <summary>
    /// Marks the projection cache stale so the next read rebuilds for the active league.
    /// Called automatically when league/team context changes.
    /// </summary>
    void Invalidate();
}
