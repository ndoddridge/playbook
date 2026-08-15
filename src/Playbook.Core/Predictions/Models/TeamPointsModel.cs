namespace Playbook.Core.Predictions.Models;

/// <summary>
/// Inputs to the team-points model. Every value is derived from real completed NFL games
/// STRICTLY PRIOR to the game being predicted — never from the game itself, and never from a
/// sportsbook line.
/// </summary>
public sealed record TeamPointsFeatures
{
    /// <summary>Mean points scored by this team over its last N completed games this season.</summary>
    public required decimal RollingPointsFor { get; init; }

    /// <summary>Mean points allowed by the opponent over its last N completed games this season.</summary>
    public required decimal OpponentRollingPointsAllowed { get; init; }

    /// <summary>True when the team is at home.</summary>
    public required bool IsHome { get; init; }

    /// <summary>Mean points scored by this team in the prior season (carry-over prior).</summary>
    public required decimal PriorSeasonPointsFor { get; init; }

    /// <summary>Completed games backing <see cref="RollingPointsFor"/>.</summary>
    public required int GamesObservedTeam { get; init; }

    /// <summary>Completed games backing <see cref="OpponentRollingPointsAllowed"/>.</summary>
    public required int GamesObservedOpponent { get; init; }
}

/// <summary>
/// Expected NFL points for one team in one game — in POINTS, not fantasy production.
/// </summary>
public sealed record TeamPointsPrediction
{
    public required decimal ExpectedPoints { get; init; }

    /// <summary>0–100, driven by how much real completed-game history backs the features.</summary>
    public required int Confidence { get; init; }

    public required string Explanation { get; init; }
}

/// <summary>
/// Ordinary least squares model mapping prior-game scoring history to expected NFL points.
///
/// PROVENANCE — these coefficients were fitted on real completed NFL games and are frozen here
/// as constants so the model is deterministic and inspectable:
///   Source   : nflverse schedules games.csv (real final scores)
///   Train    : 2020–2023 regular season, 1,758 team-games
///   Validate : 2024 regular season, 448 team-games
///   Test     : 2025 regular season, 448 team-games (fully held out)
///
/// MEASURED PERFORMANCE (mean absolute error, points per team-game):
///   2024 validation — model 7.261 vs best baseline 7.899 (rolling average)
///   2025 test       — model 7.107 vs best baseline 7.829  (−9.2%)
///   Bias on test    — −0.06 points (well centred)
/// The model beats the league-average, previous-game and rolling-average baselines on BOTH
/// held-out seasons, which is the bar for containing real information.
///
/// TARGET DISCIPLINE — the fit used ACTUAL FINAL SCORES as the target. Sportsbook lines were
/// never features and never targets. games.csv does carry spread_line/total_line columns; the
/// ingestion layer deliberately does not read them (see HistoricalGameScore), and
/// TeamPointsModelTests pins that.
///
/// KNOWN LIMIT — against the market, the model's point predictions are statistically
/// INDISTINGUISHABLE from the closing line (paired MAE, 2025: p=0.88 totals, p=0.54 spreads).
/// It beats naive baselines; it is not demonstrably sharper than a liquid market. Callers must
/// therefore treat any edge as unproven and require a wide margin before acting.
/// </summary>
public static class TeamPointsModel
{
    public const string Version = "team-points-ols-v1";

    // Fitted on 2020-2023 real final scores. Order: intercept, rollingPF, oppPA, home, priorPF.
    public const decimal Intercept = -4.9929m;
    public const decimal RollingPointsForWeight = 0.3803m;
    public const decimal OpponentPointsAllowedWeight = 0.5580m;
    public const decimal HomeFieldWeight = 1.8038m;
    public const decimal PriorSeasonWeight = 0.2349m;

    /// <summary>Minimum completed games required for BOTH teams before a prediction is offered.</summary>
    public const int MinimumGamesObserved = 3;

    /// <summary>
    /// GAME-MARKET BETTING IS DISABLED, and this constant records why.
    ///
    /// The model is genuinely good at PREDICTING POINTS — it beats the league-average,
    /// previous-game and rolling-average baselines on both held-out seasons. It is NOT good at
    /// beating a closing line, which is a different and much harder task.
    ///
    /// Backtest, exact shipped coefficients against real closing lines, 2024+2025 pooled.
    /// Break-even at −110 is 52.4%:
    ///
    ///   TOTALS                          SPREADS
    ///   edge≥2  49.6%  (278 bets)       edge≥2  48.8%  (260 bets)
    ///   edge≥3  49.8%  (205 bets)       edge≥3  48.9%  (188 bets)
    ///   edge≥4  47.4%  (133 bets)       edge≥4  50.4%  (135 bets)
    ///   edge≥5  44.7%  ( 94 bets)       edge≥5  48.8%  ( 84 bets)
    ///   edge≥6  42.4%  ( 59 bets)       edge≥6  50.9%  ( 57 bets)
    ///   edge≥7  35.5%  ( 31 bets)       edge≥7  47.4%  ( 38 bets)
    ///
    /// No threshold clears break-even. Worse, on totals the win rate FALLS monotonically as the
    /// disagreement widens — the signature of the market being right and the model being wrong.
    /// A real edge would show the opposite.
    ///
    /// Raising the threshold until a profitable-looking bucket appeared would be fitting to the
    /// evaluation data, so the honest response is to ship the projection and withhold the bet.
    /// Flip this to true only when a revised model clears break-even out-of-sample.
    /// </summary>
    public const bool GameMarketBettingEnabled = false;

    /// <summary>
    /// Edge floor that would apply if <see cref="GameMarketBettingEnabled"/> were ever enabled.
    /// Retained so the gate has a defined value; it is not reached today.
    /// </summary>
    public const decimal MinimumGameMarketEdgePoints = 3.0m;

    /// <summary>
    /// Predict expected NFL points. Returns null when the required real history does not exist —
    /// no extrapolation, no league-average fallback dressed up as a projection.
    /// </summary>
    public static TeamPointsPrediction? Predict(TeamPointsFeatures features)
    {
        ArgumentNullException.ThrowIfNull(features);

        if (features.GamesObservedTeam < MinimumGamesObserved ||
            features.GamesObservedOpponent < MinimumGamesObserved)
        {
            return null;
        }

        var points =
            Intercept
            + RollingPointsForWeight * features.RollingPointsFor
            + OpponentPointsAllowedWeight * features.OpponentRollingPointsAllowed
            + HomeFieldWeight * (features.IsHome ? 1m : 0m)
            + PriorSeasonWeight * features.PriorSeasonPointsFor;

        // An NFL team cannot score negative points. This is a domain floor, not a cosmetic clamp
        // to make the output "look right" — no upper bound is imposed.
        points = Math.Max(0m, Math.Round(points, 1, MidpointRounding.AwayFromZero));

        var evidence = Math.Min(features.GamesObservedTeam, features.GamesObservedOpponent);
        var confidence = ConfidenceFromEvidence(evidence);

        var explanation =
            $"{points} expected points — rolling PF {features.RollingPointsFor:0.0}, "
            + $"opponent PA {features.OpponentRollingPointsAllowed:0.0}, "
            + $"{(features.IsHome ? "home" : "away")}, prior season {features.PriorSeasonPointsFor:0.0}; "
            + $"{evidence} completed games of history ({Version})";

        return new TeamPointsPrediction
        {
            ExpectedPoints = points,
            Confidence = confidence,
            Explanation = explanation
        };
    }

    /// <summary>
    /// Confidence tracks how much completed-game evidence backs the features, and is capped well
    /// below certainty because the model is not demonstrably sharper than the market.
    /// </summary>
    public static int ConfidenceFromEvidence(int gamesObserved) =>
        gamesObserved switch
        {
            < MinimumGamesObserved => 0,
            3 or 4 => 40,
            5 or 6 => 50,
            7 or 8 => 56,
            _ => 60
        };
}
