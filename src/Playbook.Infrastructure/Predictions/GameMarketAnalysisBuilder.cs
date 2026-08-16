using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Assembles the analysis view of a slate's game markets: the real sportsbook lines, plus
/// Playbook's real model view where one exists.
///
/// This does NOT run a second model. It reuses the same <see cref="ITeamGameProjectionService"/>
/// that the betting path uses, so analysis and betting can never diverge — if a projection is
/// unavailable for a wager it is unavailable for analysis too, and vice versa.
/// </summary>
public static class GameMarketAnalysisBuilder
{
    public static IReadOnlyList<GameMarketAnalysis> Build(
        IReadOnlyList<PropLine> slateLines,
        ITeamGameProjectionService teamProjections,
        bool bettingEnabled)
    {
        ArgumentNullException.ThrowIfNull(slateLines);
        ArgumentNullException.ThrowIfNull(teamProjections);

        var byEvent = slateLines
            .Where(l => l.Market is PredictionMarketType.Spread
                or PredictionMarketType.GameTotal
                or PredictionMarketType.Winner
                or PredictionMarketType.TeamTotal)
            .GroupBy(l => l.Event.EventId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<GameMarketAnalysis>();

        foreach (var group in byEvent)
        {
            var gameEvent = group.First().Event;

            // Prefer the freshest line per market; never blend books silently.
            var spread = Pick(group, PredictionMarketType.Spread);
            var total = Pick(group, PredictionMarketType.GameTotal);

            var (projectedMargin, projectedTotal, unavailableReason) =
                Project(gameEvent, teamProjections);

            var hasProjection = projectedMargin is not null || projectedTotal is not null;
            var status = GameMarketAnalysisPolicy.ResolveStatus(hasProjection, bettingEnabled);

            var reason = hasProjection
                ? (bettingEnabled ? "Model projection available" : GameMarketAnalysisPolicy.BettingDisabledReason)
                : unavailableReason;

            results.Add(new GameMarketAnalysis
            {
                AwayTeam = gameEvent.AwayTeam,
                HomeTeam = gameEvent.HomeTeam,
                CommenceTime = gameEvent.CommenceTime,
                SpreadLine = spread?.Line,
                TotalLine = total?.Line,
                Bookmaker = (spread ?? total)?.Bookmaker,
                ProjectedHomeMargin = projectedMargin,
                ProjectedTotal = projectedTotal,
                Status = status,
                Reason = reason
            });
        }

        return results
            .OrderBy(r => r.CommenceTime)
            .ThenBy(r => r.Matchup, StringComparer.Ordinal)
            .ToList();
    }

    private static PropLine? Pick(IEnumerable<PropLine> lines, PredictionMarketType market) =>
        lines
            .Where(l => l.Market == market && l.Line is not null)
            .OrderByDescending(l => l.UpdatedAt)
            .FirstOrDefault();

    /// <summary>
    /// Ask the shared projection service for both teams. Returns nulls plus the service's own
    /// stated reason when unavailable — the reason is never invented here.
    /// </summary>
    private static (decimal? Margin, decimal? Total, string Reason) Project(
        FootballEvent gameEvent,
        ITeamGameProjectionService teamProjections)
    {
        var homeAbbr = NflTeamCatalog.ResolveAbbreviations(gameEvent.HomeTeam).FirstOrDefault();
        var awayAbbr = NflTeamCatalog.ResolveAbbreviations(gameEvent.AwayTeam).FirstOrDefault();

        if (string.IsNullOrEmpty(homeAbbr) || string.IsNullOrEmpty(awayAbbr))
        {
            return (null, null, "Team identity could not be resolved for this game");
        }

        var home = teamProjections.GetTeamProjection(homeAbbr, gameEvent, gameEvent.Phase);
        var away = teamProjections.GetTeamProjection(awayAbbr, gameEvent, gameEvent.Phase);

        if (home.Confidence == 0 || away.Confidence == 0)
        {
            // Surface the projection service's real explanation (preseason uncertainty,
            // insufficient completed games, missing score data, ...) rather than a generic string.
            var reason = home.Confidence == 0 ? home.Reasoning : away.Reasoning;
            return (null, null, string.IsNullOrWhiteSpace(reason) ? "Model projection unavailable" : reason);
        }

        var margin = Math.Round(home.EstimatedTeamScore - away.EstimatedTeamScore, 1, MidpointRounding.AwayFromZero);
        var total = Math.Round(home.EstimatedTeamScore + away.EstimatedTeamScore, 1, MidpointRounding.AwayFromZero);

        return (margin, total, "Model projection available");
    }
}
