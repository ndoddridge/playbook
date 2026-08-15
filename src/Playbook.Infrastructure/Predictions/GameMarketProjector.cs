using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Game-market projection for Quick Picks (Spread, Total, Moneyline, Team Total).
/// Complements PropStatProjector (player props) by estimating team-level scoring.
/// Regular season: aggregates player data for defensible estimates.
/// Preseason: returns unavailable to avoid fabricated lineup/snap estimates.
/// </summary>
public static class GameMarketProjector
{
    public static (decimal? Projection, int Confidence, int Volatility) ProjectGameMarket(
        PredictionMarketType market,
        FootballEvent gameEvent,
        PropLine line,
        ITeamGameProjectionService teamProjectionService,
        NflSeasonPhase seasonPhase)
    {
        ArgumentNullException.ThrowIfNull(teamProjectionService);
        ArgumentNullException.ThrowIfNull(gameEvent);
        ArgumentNullException.ThrowIfNull(line);

        // Only handles game markets; caller must route player props to PropStatProjector.
        if (market is not (PredictionMarketType.GameTotal
            or PredictionMarketType.TeamTotal
            or PredictionMarketType.Winner
            or PredictionMarketType.Spread))
        {
            return (null, 0, 100);
        }

        // Preseason: defer team projections (roster/snap uncertainty too high).
        if (seasonPhase == NflSeasonPhase.Preseason)
        {
            return (null, 0, 100);
        }

        // Regular season: attempt team aggregation.
        // Route based on market type.
        return market switch
        {
            PredictionMarketType.TeamTotal =>
                EstimateTeamTotal(gameEvent, line, teamProjectionService, seasonPhase),

            PredictionMarketType.GameTotal =>
                EstimateGameTotal(gameEvent, line, teamProjectionService, seasonPhase),

            PredictionMarketType.Spread =>
                EstimateSpread(gameEvent, line, teamProjectionService, seasonPhase),

            PredictionMarketType.Winner =>
                EstimateMoneyline(gameEvent, line, teamProjectionService, seasonPhase),

            _ => (null, 0, 100)
        };
    }

    /// <summary>
    /// The home margin the market implies, given a home spread line.
    ///
    /// TheOddsAPI publishes the spread from the home team's perspective, so a home favourite is
    /// a NEGATIVE point value (home −6.5 ⇒ the market expects home to win by 6.5). A projected
    /// margin, by contrast, is POSITIVE when home is better. The two must be put in the same
    /// convention before any edge is computed:
    ///
    ///     edge = projectedHomeMargin − MarketImpliedHomeMargin(line)
    ///
    /// Comparing a positive projected margin directly against the raw negative line — which is
    /// what the pipeline would do today — roughly doubles the apparent edge and would flag every
    /// home favourite as a value bet. This helper exists so the conversion is defined and tested
    /// now; it is wired in as part of the points-calibration work, since the spread path is
    /// withheld until then (see TeamGameProjectionService).
    /// </summary>
    public static decimal MarketImpliedHomeMargin(decimal homeSpreadLine) => -homeSpreadLine;

    /// <summary>Get the home team abbreviation from game event.</summary>
    private static string GetHomeTeamAbbreviation(FootballEvent gameEvent)
    {
        var abbr = NflTeamCatalog.ResolveAbbreviations(gameEvent.HomeTeam);
        return abbr.FirstOrDefault() ?? "";
    }

    /// <summary>Get the away team abbreviation from game event.</summary>
    private static string GetAwayTeamAbbreviation(FootballEvent gameEvent)
    {
        var abbr = NflTeamCatalog.ResolveAbbreviations(gameEvent.AwayTeam);
        return abbr.FirstOrDefault() ?? "";
    }

    private static (decimal? Projection, int Confidence, int Volatility) EstimateTeamTotal(
        FootballEvent gameEvent,
        PropLine line,
        ITeamGameProjectionService projectionService,
        NflSeasonPhase seasonPhase)
    {
        // Team total: identify which team from the prop line.
        var teamAbbr = !string.IsNullOrEmpty(line.TeamName)
            ? NflTeamCatalog.ResolveAbbreviations(line.TeamName).FirstOrDefault()
            : null;

        if (string.IsNullOrEmpty(teamAbbr))
        {
            return (null, 0, 100);
        }

        var teamProj = projectionService.GetTeamProjection(teamAbbr, gameEvent, seasonPhase);
        if (teamProj.Confidence == 0)
        {
            return (null, 0, 100);
        }

        return (teamProj.EstimatedTeamScore, teamProj.Confidence, teamProj.Volatility);
    }

    private static (decimal? Projection, int Confidence, int Volatility) EstimateGameTotal(
        FootballEvent gameEvent,
        PropLine line,
        ITeamGameProjectionService projectionService,
        NflSeasonPhase seasonPhase)
    {
        // Game total = home + away estimated scores.
        var homeAbbr = GetHomeTeamAbbreviation(gameEvent);
        var awayAbbr = GetAwayTeamAbbreviation(gameEvent);

        if (string.IsNullOrEmpty(homeAbbr) || string.IsNullOrEmpty(awayAbbr))
        {
            return (null, 0, 100);
        }

        var homeTeamProj = projectionService.GetTeamProjection(homeAbbr, gameEvent, seasonPhase);
        if (homeTeamProj.Confidence == 0)
        {
            return (null, 0, 100);
        }

        var awayTeamProj = projectionService.GetTeamProjection(awayAbbr, gameEvent, seasonPhase);
        if (awayTeamProj.Confidence == 0)
        {
            return (null, 0, 100);
        }

        var totalProjection = homeTeamProj.EstimatedTeamScore + awayTeamProj.EstimatedTeamScore;
        var avgConf = (homeTeamProj.Confidence + awayTeamProj.Confidence) / 2;
        var maxVol = Math.Max(homeTeamProj.Volatility, awayTeamProj.Volatility);

        return (totalProjection, avgConf, maxVol);
    }

    private static (decimal? Projection, int Confidence, int Volatility) EstimateSpread(
        FootballEvent gameEvent,
        PropLine line,
        ITeamGameProjectionService projectionService,
        NflSeasonPhase seasonPhase)
    {
        // Spread = home - away (when positive, home is favored).
        var homeAbbr = GetHomeTeamAbbreviation(gameEvent);
        var awayAbbr = GetAwayTeamAbbreviation(gameEvent);

        if (string.IsNullOrEmpty(homeAbbr) || string.IsNullOrEmpty(awayAbbr))
        {
            return (null, 0, 100);
        }

        var homeTeamProj = projectionService.GetTeamProjection(homeAbbr, gameEvent, seasonPhase);
        if (homeTeamProj.Confidence == 0)
        {
            return (null, 0, 100);
        }

        var awayTeamProj = projectionService.GetTeamProjection(awayAbbr, gameEvent, seasonPhase);
        if (awayTeamProj.Confidence == 0)
        {
            return (null, 0, 100);
        }

        var spreadProjection = homeTeamProj.EstimatedTeamScore - awayTeamProj.EstimatedTeamScore;
        var avgConf = (homeTeamProj.Confidence + awayTeamProj.Confidence) / 2;
        var maxVol = Math.Max(homeTeamProj.Volatility, awayTeamProj.Volatility);

        return (spreadProjection, avgConf, maxVol);
    }

    private static (decimal? Projection, int Confidence, int Volatility) EstimateMoneyline(
        FootballEvent gameEvent,
        PropLine line,
        ITeamGameProjectionService projectionService,
        NflSeasonPhase seasonPhase)
    {
        // Moneyline (Winner): home wins if spread > 0. Return the spread as proxy.
        var homeAbbr = GetHomeTeamAbbreviation(gameEvent);
        var awayAbbr = GetAwayTeamAbbreviation(gameEvent);

        if (string.IsNullOrEmpty(homeAbbr) || string.IsNullOrEmpty(awayAbbr))
        {
            return (null, 0, 100);
        }

        var homeTeamProj = projectionService.GetTeamProjection(homeAbbr, gameEvent, seasonPhase);
        if (homeTeamProj.Confidence == 0)
        {
            return (null, 0, 100);
        }

        var awayTeamProj = projectionService.GetTeamProjection(awayAbbr, gameEvent, seasonPhase);
        if (awayTeamProj.Confidence == 0)
        {
            return (null, 0, 100);
        }

        // Moneyline edge is typically the spread, but normalized to ±0.5 for direction.
        // For now, use spread as the projection (engine will map to direction).
        var spreadProjection = homeTeamProj.EstimatedTeamScore - awayTeamProj.EstimatedTeamScore;
        var avgConf = (homeTeamProj.Confidence + awayTeamProj.Confidence) / 2;
        var maxVol = Math.Max(homeTeamProj.Volatility, awayTeamProj.Volatility);

        return (spreadProjection, avgConf, maxVol);
    }
}
