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
/// Expected NFL points for one team in one game.
///
/// Runs the calibrated <see cref="TeamPointsModel"/>, which was fitted on real completed NFL
/// final scores (nflverse schedules) and validated on two held-out seasons. Output is in POINTS
/// and is therefore directly comparable to a sportsbook total or spread.
///
/// This deliberately does NOT use the fantasy-production aggregate. That path was withheld in
/// 70b0428 because fantasy points are not NFL points; the fix was to model points directly from
/// completed-game scoring rather than to invent a conversion. <see cref="BuildProductionIndex"/>
/// is retained for observability of the player-level inputs and is not part of the points path.
///
/// Returns Unavailable — never an extrapolation — whenever the required completed-game history
/// does not exist, which includes all of preseason and the opening weeks of a season.
/// </summary>
public sealed class TeamGameProjectionService : ITeamGameProjectionService
{
    private readonly IPlayerService _playerService;
    private readonly IProjectionService _projectionService;
    private readonly IPlayerInjuryService _injuryService;
    private readonly IIntelligenceService _intelligenceService;
    private readonly IPlayerStatisticalContextService _statsService;
    private readonly IHistoricalGameScoreProvider _scores;
    private readonly IQuarterbackFormProvider _quarterbacks;
    private readonly ILogger<TeamGameProjectionService> _logger;

    /// <summary>
    /// Conservative baseline, anchored to measured error rather than invented: the model's
    /// held-out 2025 RMSE was 8.8 points against a league SD of ~10, i.e. it explains only part
    /// of the variance. Not a learned volatility model.
    /// </summary>
    internal const int TeamPointsVolatility = 55;

    public TeamGameProjectionService(
        IPlayerService playerService,
        IProjectionService projectionService,
        IPlayerInjuryService injuryService,
        IIntelligenceService intelligenceService,
        IPlayerStatisticalContextService statsService,
        IHistoricalGameScoreProvider scores,
        IQuarterbackFormProvider quarterbacks,
        ILogger<TeamGameProjectionService> logger)
    {
        _playerService = playerService;
        _projectionService = projectionService;
        _injuryService = injuryService;
        _intelligenceService = intelligenceService;
        _statsService = statsService;
        _scores = scores;
        _quarterbacks = quarterbacks;
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
            // Real NFL points, from the calibrated team-points model fitted on real final scores.
            // The fantasy-production index is NOT used here — that units gap (70b0428) is exactly
            // why this path now runs on completed-game scoring instead.
            var completed = _scores.GetCompletedGames(gameEvent.Season);
            if (completed.Count == 0)
            {
                return TeamGameProjection.Unavailable(
                    "Historical NFL scores unavailable — team-points model cannot run.");
            }

            var isHome = string.Equals(gameEvent.HomeTeam, teamAbbreviation, StringComparison.OrdinalIgnoreCase);
            var opponent = isHome ? gameEvent.AwayTeam : gameEvent.HomeTeam;

            var features = TeamPointsFeatureBuilder.Build(
                teamAbbreviation, opponent, isHome, gameEvent.Season, gameEvent.Week, completed);

            // Player-level evidence: quarterback quality from completed games only. Absent data
            // leaves the feature null, which selects the untouched baseline coefficients.
            if (features is not null)
            {
                var qbForm = QuarterbackFormBuilder.Build(
                    teamAbbreviation,
                    gameEvent.Season,
                    gameEvent.Week,
                    _quarterbacks.GetQuarterbackLines(gameEvent.Season));

                if (qbForm is not null)
                {
                    features = features with { QuarterbackEpaPerAttempt = qbForm.EpaPerAttempt };
                }
            }

            if (features is null)
            {
                return TeamGameProjection.Unavailable(
                    $"{teamAbbreviation}: no completed games this season yet — team-points model needs "
                    + $"{TeamPointsModel.MinimumGamesObserved}+ games for both teams.");
            }

            var prediction = TeamPointsModel.Predict(features);
            if (prediction is null)
            {
                return TeamGameProjection.Unavailable(
                    $"{teamAbbreviation}: only {features.GamesObservedTeam} team / "
                    + $"{features.GamesObservedOpponent} opponent completed games — model requires "
                    + $"{TeamPointsModel.MinimumGamesObserved} of each. No extrapolation.");
            }

            return new TeamGameProjection
            {
                TeamAbbreviation = teamAbbreviation,
                EstimatedTeamScore = prediction.ExpectedPoints,
                Confidence = prediction.Confidence,
                Volatility = TeamPointsVolatility,
                Reasoning = prediction.Explanation
            };
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
