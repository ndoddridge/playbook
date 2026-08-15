using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Players;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;
using Microsoft.Extensions.Logging;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Estimates team-level game scoring by aggregating player projections with real intelligence factors.
/// Regular season: uses player projections + health/usage/trend adjustments.
/// Preseason: conservative, unavailable unless strong roster data available.
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

        // Preseason: do not fabricate lineup/snap assumptions.
        if (seasonPhase == NflSeasonPhase.Preseason)
        {
            return TeamGameProjection.Unavailable(
                "Team projections unavailable during preseason (roster/lineup uncertainty too high).");
        }

        // Regular season: aggregate player projections with real intelligence factors.
        try
        {
            var projections = _projectionService.GetAllProjections();
            if (projections.Count == 0)
            {
                return TeamGameProjection.Unavailable("No player projections available");
            }

            // Collect offensive players on this team, split by position.
            var qbs = new List<(PlayerProjection proj, Player player)>();
            var rbsWrsTeS = new List<(PlayerProjection proj, Player player)>();

            foreach (var proj in projections)
            {
                var player = _playerService.GetPlayer(proj.PlayerId);
                if (player is not null &&
                    string.Equals(player.Team, teamAbbreviation, StringComparison.OrdinalIgnoreCase) &&
                    player.Status == PlayerStatus.Active)
                {
                    if (player.Position == Position.QB)
                    {
                        qbs.Add((proj, player));
                    }
                    else if (player.Position is Position.RB or Position.WR or Position.TE)
                    {
                        rbsWrsTeS.Add((proj, player));
                    }
                }
            }

            if (rbsWrsTeS.Count == 0)
            {
                return TeamGameProjection.Unavailable($"No active RB/WR/TE found for {teamAbbreviation}");
            }

            // QB adjustment: critical for team scoring.
            var qbMultiplier = 1m;
            if (qbs.Count > 0)
            {
                var qb = qbs[0]; // Primary QB
                var qbInjury = _injuryService.GetCurrentInjury(qb.player.Id);
                var qbIntel = _intelligenceService.GetPlayerProfile(qb.player.Id);

                // If QB is out/injured, apply significant reduction.
                if (qbInjury?.IsOutOrSidelined() ?? false)
                {
                    qbMultiplier = 0.75m; // Backup QB is typically 75% as effective
                }
                else if (qbIntel?.HealthScore < 40)
                {
                    qbMultiplier = 0.90m; // Injured but playing is slightly reduced
                }
            }
            else
            {
                // No QB on roster (unusual)
                qbMultiplier = 0.75m;
            }

            // Aggregate RB/WR/TE with health-score weighting.
            decimal totalProjection = 0;
            int projectionCount = 0;

            foreach (var (proj, player) in rbsWrsTeS)
            {
                var adjustedProj = proj.ProjectedPoints;

                // Health-score adjustment: lower health score = lower expected production.
                var playerIntel = _intelligenceService.GetPlayerProfile(player.Id);
                if (playerIntel is not null)
                {
                    // Health score 50 = neutral, < 50 = hurt, > 50 = healthy
                    var healthMultiplier = (playerIntel.HealthScore - 25m) / 100m; // Range: 0.25 to 0.75
                    healthMultiplier = Math.Clamp(healthMultiplier, 0.60m, 1.15m); // Don't go too extreme
                    adjustedProj = Math.Round(adjustedProj * healthMultiplier, 1);
                }

                // Recent trend adjustment: if player's usage is trending down, reduce projection.
                var statsCtx = _statsService.GetContext(player.Id);
                if (statsCtx?.Trend == StatisticalTrendSignal.Decreasing)
                {
                    adjustedProj = Math.Round(adjustedProj * 0.95m, 1);
                }
                else if (statsCtx?.Trend == StatisticalTrendSignal.Increasing)
                {
                    adjustedProj = Math.Round(adjustedProj * 1.05m, 1);
                }

                totalProjection += adjustedProj;
                projectionCount++;
            }

            // Apply QB multiplier to the full team projection.
            totalProjection = Math.Round(totalProjection * qbMultiplier, 1);

            // Confidence based on:
            // 1. Coverage: have projections for most starters?
            // 2. Injury: any key players out?
            // 3. Trend stability: is the team's offensive production stable?
            var coverageRatio = (decimal)rbsWrsTeS.Count / 8m; // Expect ~8 key positions
            var baseCoverage = Math.Clamp(coverageRatio, 0.5m, 1m) * 100m;

            var injuryImpact = 0; // 0 = no impact, -10 = some key player out, etc.
            var activeInjuries = rbsWrsTeS.Count(pair =>
            {
                var inj = _injuryService.GetCurrentInjury(pair.player.Id);
                return inj?.IsOutOrSidelined() ?? false;
            });
            if (activeInjuries > 0)
            {
                injuryImpact = -Math.Min(activeInjuries * 8, 20);
            }

            var confidence = (int)Math.Clamp(baseCoverage * 0.75m + 50m + injuryImpact, 25, 75);

            var volatility = 48; // Slightly higher due to team-level unpredictability

            var reasoning = $"Aggregated {projectionCount} players ({rbsWrsTeS.Count} skill); " +
                          $"QB multiplier {qbMultiplier:0.00}; health-adjusted; recent trends applied; " +
                          $"proj {totalProjection} pts · conf {confidence}%";

            if (totalProjection < 8 || confidence < 25)
            {
                return TeamGameProjection.Unavailable($"Insufficient quality: {reasoning}");
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

/// <summary>Extension methods for injury data.</summary>
internal static class InjuryExtensions
{
    internal static bool IsOutOrSidelined(this Core.Injuries.Models.PlayerInjuryRecord injury)
    {
        if (injury?.Status is null)
            return false;
        // Out / IR / PUP / Suspension are all sidelining statuses.
        return injury.Status.Contains("Out", StringComparison.OrdinalIgnoreCase) ||
               injury.Status.Contains("IR", StringComparison.OrdinalIgnoreCase) ||
               injury.Status.Contains("PUP", StringComparison.OrdinalIgnoreCase) ||
               injury.Status.Contains("Suspension", StringComparison.OrdinalIgnoreCase);
    }
}
