using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Converts calibrated team-point projections into game-market numbers.
///
/// Team Total = that team's expected points.
/// Game Total = home + away expected points.
/// Spread     = projected home line, in the book's own sign convention.
/// Moneyline  = DISABLED — a point margin is not a win probability (see EstimateMoneyline).
///
/// All values originate from TeamPointsModel, fitted on real completed NFL scores. Preseason
/// returns nothing: participation dominates preseason outcomes and Playbook cannot observe it.
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
    /// Comparing a positive projected margin directly against the raw negative line roughly
    /// doubles the apparent edge and would flag every home favourite as a value bet. EstimateSpread
    /// therefore emits its projection already in the book's convention.
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

        // Convention: the book publishes the HOME line, negative when home is favoured
        // (home -6.5). A projected margin is positive when home is better. Emitting the raw
        // margin would make the engine compute (+8) - (-6.5) = 14.5 for what is really a
        // 1.5-point disagreement. Emit in the book's convention so (projection - line) is the
        // true edge, and the engine's Cover/NotCover mapping stays correct: a projection more
        // negative than the line means home covers by more than the market expects.
        var projectedMargin = homeTeamProj.EstimatedTeamScore - awayTeamProj.EstimatedTeamScore;
        var projectedHomeLine = -projectedMargin;

        var avgConf = (homeTeamProj.Confidence + awayTeamProj.Confidence) / 2;
        var maxVol = Math.Max(homeTeamProj.Volatility, awayTeamProj.Volatility);

        return (projectedHomeLine, avgConf, maxVol);
    }

    private static (decimal? Projection, int Confidence, int Volatility) EstimateMoneyline(
        FootballEvent gameEvent,
        PropLine line,
        ITeamGameProjectionService projectionService,
        NflSeasonPhase seasonPhase)
    {
        // MONEYLINE IS DISABLED.
        //
        // QuickPicksEngine compares the projection against a 0.5 constant for Winner markets
        // (ResolveComparableLine), i.e. it expects a probability. A point margin is not a
        // probability, and Playbook has no calibrated margin-to-win-probability mapping: doing
        // that honestly needs the historical relationship between projected margin and actual
        // win rate, which has not been fitted or validated.
        //
        // Feeding a margin into a probability slot would make every positive-margin game read as
        // a maximum-confidence home bet. Returning no projection keeps moneyline at NO PLAY until
        // a real win-probability calibration exists.
        _ = gameEvent;
        _ = line;
        _ = projectionService;
        _ = seasonPhase;
        return (null, 0, 100);
    }
}