using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;

namespace Playbook.Application.Projections.Interfaces;

/// <summary>
/// Deterministic, explainable Projection Engine v0.1.
/// Data → Intelligence → Projection. Does not scrape providers or parse news.
/// </summary>
public interface IProjectionEngine
{
    string Version { get; }

    PlayerProjection Project(
        Player player,
        PlayerProductionSnapshot production,
        PlayerIntelligenceProfile? intelligence,
        ProjectionLeagueContext leagueContext,
        PlayerInjuryRecord? currentInjury = null,
        PlayerStatisticalContext? statisticalContext = null,
        MatchupContext? matchup = null,
        GameEnvironmentContext? gameEnvironment = null);

    IReadOnlyList<PlayerProjection> ProjectMany(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Guid, PlayerProductionSnapshot> productionByPlayer,
        IReadOnlyDictionary<Guid, PlayerIntelligenceProfile> intelligenceByPlayer,
        ProjectionLeagueContext leagueContext,
        IReadOnlyDictionary<Guid, PlayerInjuryRecord>? currentInjuriesByPlayer = null,
        IReadOnlyDictionary<Guid, PlayerStatisticalContext>? statisticalContextByPlayer = null,
        IReadOnlyDictionary<Guid, MatchupContext>? matchupByPlayer = null,
        IReadOnlyDictionary<Guid, GameEnvironmentContext>? environmentByPlayer = null);
}
