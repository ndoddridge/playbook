using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Application.Projections.Interfaces;

/// <summary>
/// Deterministic, explainable projection rules.
/// Consumes intelligence + player + league context; does not make fantasy decisions.
/// </summary>
public interface IProjectionEngine
{
    PlayerProjection Project(
        Player player,
        PlayerIntelligenceProfile? intelligence,
        ProjectionLeagueContext leagueContext);

    IReadOnlyList<PlayerProjection> ProjectMany(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Guid, PlayerIntelligenceProfile> intelligenceByPlayer,
        ProjectionLeagueContext leagueContext);
}
