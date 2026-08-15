using Microsoft.Extensions.Logging;
using Playbook.Application.Players;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Estimates team-level game scoring by aggregating player projections.
/// Regular season: uses player fantasy projections + production data.
/// Preseason: returns unavailable (insufficient lineup certainty).
/// </summary>
public sealed class TeamGameProjectionService : ITeamGameProjectionService
{
    private readonly IPlayerService _playerService;
    private readonly IProjectionService _projectionService;
    private readonly ILogger<TeamGameProjectionService> _logger;

    public TeamGameProjectionService(
        IPlayerService playerService,
        IProjectionService projectionService,
        ILogger<TeamGameProjectionService> logger)
    {
        _playerService = playerService;
        _projectionService = projectionService;
        _logger = logger;
    }

    public TeamGameProjection GetTeamProjection(
        string teamAbbreviation,
        FootballEvent gameEvent,
        NflSeasonPhase seasonPhase)
    {
        if (string.IsNullOrWhiteSpace(teamAbbreviation))
        {
            return TeamGameProjection.Unavailable("Empty team abbreviation");
        }

        // Preseason: do not fabricate lineup/snap assumptions.
        if (seasonPhase == NflSeasonPhase.Preseason)
        {
            return TeamGameProjection.Unavailable(
                "Team projections unavailable during preseason (roster/lineup uncertainty too high).");
        }

        // Regular season: aggregate player projections for known starters.
        try
        {
            var projections = _projectionService.GetAllProjections();
            if (projections.Count == 0)
            {
                return TeamGameProjection.Unavailable("No player projections available");
            }

            // Filter to players on this team who are projected to play.
            var teamPlayers = new List<PlayerProjection>();
            foreach (var proj in projections)
            {
                var player = _playerService.GetPlayer(proj.PlayerId);
                if (player is not null &&
                    string.Equals(player.Team, teamAbbreviation, StringComparison.OrdinalIgnoreCase) &&
                    player.Status == PlayerStatus.Active &&
                    (player.Position == Position.QB ||
                     player.Position == Position.RB ||
                     player.Position == Position.WR ||
                     player.Position == Position.TE))
                {
                    teamPlayers.Add(proj);
                }
            }

            if (teamPlayers.Count == 0)
            {
                return TeamGameProjection.Unavailable($"No active offensive players found for {teamAbbreviation}");
            }

            // Sum projected fantasy points (rough proxy for game scoring).
            // Fantasy points are scoring-format-independent PPR/standard approximation.
            var totalProjection = Math.Round(teamPlayers.Sum(p => p.ProjectedPoints), 1);

            // Confidence based on data completeness and projection freshness.
            var coverageRatio = (decimal)teamPlayers.Count / 10m; // Expect ~10 offensive starters
            var baseCoverage = Math.Min(coverageRatio, 1m) * 100m;
            var confidence = (int)Math.Clamp(baseCoverage * 0.7m + 50m, 30, 75);

            // Volatility higher during regular season without full injury/depth data.
            var volatility = 45;

            var reasoning = $"Aggregated {teamPlayers.Count} active offensive players; " +
                          $"total projected {totalProjection} fantasy points · confidence {confidence}% ";

            if (totalProjection < 10 || confidence < 30)
            {
                return TeamGameProjection.Unavailable($"Insufficient projection data: {reasoning}");
            }

            return new TeamGameProjection
            {
                TeamAbbreviation = teamAbbreviation,
                EstimatedTeamScore = totalProjection,
                Confidence = confidence,
                Volatility = volatility,
                Reasoning = reasoning
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Team projection failed for {Team}", teamAbbreviation);
            return TeamGameProjection.Unavailable($"Projection service error: {ex.Message}");
        }
    }
}
