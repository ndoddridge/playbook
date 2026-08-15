using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;

namespace Playbook.Application.Predictions.Interfaces;

/// <summary>
/// Estimates team-level game scoring for Quick Picks game markets (Spread, Total, etc.).
/// Regular season uses player-level data aggregation. Preseason returns unavailable
/// (roster/lineup certainty insufficient for defensible estimates).
/// </summary>
public interface ITeamGameProjectionService
{
    /// <summary>
    /// Project a team's total points in an upcoming game.
    /// Regular season: uses player projections and production data.
    /// Preseason: returns unavailable (insufficient lineup certainty).
    /// </summary>
    TeamGameProjection GetTeamProjection(
        string teamAbbreviation,
        FootballEvent gameEvent,
        NflSeasonPhase seasonPhase);
}
