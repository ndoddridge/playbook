using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Application.Projections.Interfaces;

/// <summary>
/// Deterministic, explainable projection rules.
/// Consumes player-specific production + intelligence + league context.
/// Does not make fantasy decisions.
/// </summary>
public interface IProjectionEngine
{
    PlayerProjection Project(
        Player player,
        PlayerProductionSnapshot production,
        PlayerIntelligenceProfile? intelligence,
        ProjectionLeagueContext leagueContext,
        PlayerInjuryRecord? currentInjury = null);

    IReadOnlyList<PlayerProjection> ProjectMany(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Guid, PlayerProductionSnapshot> productionByPlayer,
        IReadOnlyDictionary<Guid, PlayerIntelligenceProfile> intelligenceByPlayer,
        ProjectionLeagueContext leagueContext,
        IReadOnlyDictionary<Guid, PlayerInjuryRecord>? currentInjuriesByPlayer = null);
}
