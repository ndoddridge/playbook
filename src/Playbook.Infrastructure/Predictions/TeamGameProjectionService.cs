using Microsoft.Extensions.Logging;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Players;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Builds a team's aggregate offensive production from real player projections, injuries and
/// intelligence, then decides whether that aggregate can honestly answer a game market.
///
/// It currently cannot, and says so. The aggregate is weekly <em>fantasy</em> points in the
/// connected league's scoring format; a sportsbook total/spread is in NFL points. Playbook has
/// no validated conversion between the two, so this returns Unavailable rather than emitting a
/// number in the wrong units. The aggregation itself is still computed and reported, so the
/// inputs stay observable and the only missing piece is the calibration.
/// </summary>
public sealed class TeamGameProjectionService : ITeamGameProjectionService
{
    private readonly IPlayerService _playerService;
    private readonly IProjectionService _projectionService;
    private readonly IPlayerInjuryService _injuryService;
    private readonly IIntelligenceService _intelligenceService;
    private readonly IPlayerStatisticalContextService _statsService;
    private readonly ILogger<TeamGameProjectionService> _logger;

    public TeamGameProjectionService(
        IPlayerService playerService,
        IProjectionService projectionService,
        IPlayerInjuryService injuryService,
        IIntelligenceService intelligenceService,
        IPlayerStatisticalContextService statsService,
        ILogger<TeamGameProjectionService> logger)
    {
        _playerService = playerService;
        _projectionService = projectionService;
        _injuryService = injuryService;
        _intelligenceService = intelligenceService;
        _statsService = statsService;
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

        // Preseason: roster/snap uncertainty is too high to project a team at all.
        if (seasonPhase == NflSeasonPhase.Preseason)
        {
            return TeamGameProjection.Unavailable(
                "Team projections unavailable during preseason (roster/lineup uncertainty too high).");
        }

        try
        {
            var index = BuildProductionIndex(teamAbbreviation);
            if (index is null)
            {
                return TeamGameProjection.Unavailable(
                    $"Insufficient player data for {teamAbbreviation} (need a quarterback and at least one skill player).");
            }

            // ---------------------------------------------------------------------------
            // UNIT GUARD — the reason no game-market pick is produced today.
            //
            // index.FantasyProductionPoints is a sum of weekly fantasy points in the connected
            // league's scoring format. A sportsbook total/spread is in NFL points. These are
            // different units: the aggregate moves when the user's league switches PPR →
            // Standard, and PPR reception points have no scoreboard equivalent at all.
            //
            // Comparing them would put a ~75-point aggregate against a ~45-point total and
            // produce a maximum-edge OVER on every game on the slate. Dividing by a guessed
            // constant, or fitting to the sportsbook line, would both be fabrication — the line
            // is the market we are trying to beat, not an input.
            //
            // So: report the real aggregate, withhold the pick, and wait for a real calibration.
            // ---------------------------------------------------------------------------
            return TeamGameProjection.Unavailable(
                $"{teamAbbreviation}: offensive production index {index.FantasyProductionPoints} fantasy pts " +
                $"({index.Explanation}). No validated conversion from fantasy production to NFL points " +
                "exists yet, so this cannot be compared against a sportsbook points line.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Team production index failed for {Team}", teamAbbreviation);
            return TeamGameProjection.Unavailable($"Projection service error: {ex.Message}");
        }
    }

    /// <summary>
    /// Gather this team's real per-player inputs and aggregate them.
    /// Internal so the data-gathering path is testable independently of the unit guard.
    /// </summary>
    internal TeamProductionIndex? BuildProductionIndex(string teamAbbreviation)
    {
        var projections = _projectionService.GetAllProjections();
        if (projections.Count == 0)
        {
            return null;
        }

        var inputs = new List<TeamPlayerProductionInput>();

        foreach (var projection in projections)
        {
            var player = _playerService.GetPlayer(projection.PlayerId);
            if (player is null ||
                !string.Equals(player.Team, teamAbbreviation, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (player.Position is not (Position.QB or Position.RB or Position.WR or Position.TE))
            {
                continue;
            }

            // Roster status and injury designation both rule a player out. Previously a
            // sidelined player was still counted at full projected value, which inflated the
            // aggregate by the exact production that will not happen.
            var ruledOutByRoster = player.Status
                is PlayerStatus.InjuredReserve
                or PlayerStatus.Suspended
                or PlayerStatus.PracticeSquad;

            var ruledOutByInjury = _injuryService.GetCurrentInjury(player.Id)?.IsOutOrSidelined() ?? false;

            inputs.Add(new TeamPlayerProductionInput
            {
                Position = player.Position,
                ProjectedFantasyPoints = projection.ProjectedPoints,
                IsRuledOut = ruledOutByRoster || ruledOutByInjury,
                HealthScore = _intelligenceService.GetPlayerProfile(player.Id)?.HealthScore,
                Trend = _statsService.GetContext(player.Id)?.Trend ?? StatisticalTrendSignal.Unknown
            });
        }

        return inputs.Count == 0 ? null : TeamProductionIndexCalculator.Compute(inputs);
    }
}

/// <summary>Injury designation helpers.</summary>
internal static class InjuryExtensions
{
    internal static bool IsOutOrSidelined(this Core.Injuries.Models.PlayerInjuryRecord injury)
    {
        if (injury?.Status is null)
        {
            return false;
        }

        return injury.Status.Contains("Out", StringComparison.OrdinalIgnoreCase) ||
               injury.Status.Contains("IR", StringComparison.OrdinalIgnoreCase) ||
               injury.Status.Contains("PUP", StringComparison.OrdinalIgnoreCase) ||
               injury.Status.Contains("Suspension", StringComparison.OrdinalIgnoreCase);
    }
}
