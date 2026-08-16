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

    /// <summary>
    /// Attempt-weighted passing EPA per attempt for this team's quarterbacks over prior games
    /// this season. Null when no prior passing work exists, which selects the baseline
    /// coefficient set rather than substituting a league-average quarterback.
    /// </summary>
    public decimal? QuarterbackEpaPerAttempt { get; init; }
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

    // Chronological evaluation split. Encoded as constants so the methodology is part of the
    // code contract and cannot drift: no shuffling, no overlap, validation and test strictly
    // after training. Every model version is fitted and compared on exactly this split.
    public const int TrainSeasonStart = 2020;
    public const int TrainSeasonEnd = 2023;
    public const int ValidationSeason = 2024;
    public const int TestSeason = 2025;

    // Fitted on 2020-2023 real final scores. Order: intercept, rollingPF, oppPA, home, priorPF.
    public const decimal Intercept = -4.9929m;
    public const decimal RollingPointsForWeight = 0.3803m;
    public const decimal OpponentPointsAllowedWeight = 0.5580m;
    public const decimal HomeFieldWeight = 1.8038m;
    public const decimal PriorSeasonWeight = 0.2349m;

    /// <summary>Minimum completed games required for BOTH teams before a prediction is offered.</summary>
    public const int MinimumGamesObserved = 3;

    // ---------------------------------------------------------------------------------------
    // QB-ENHANCED COEFFICIENTS (v2 path)
    //
    // Same data, same chronological split, same target (real final scores). Adds one feature:
    // attempt-weighted passing EPA per attempt over prior games this season.
    //
    // MEASURED TWO WAYS. Both improve on both held-out seasons; the magnitudes differ because
    // they answer different questions.
    //
    // (a) PRODUCTION BEHAVIOUR — shipped v1 vs shipped v2, i.e. exactly what this class does at
    //     runtime. Verified by QuarterbackBacktestTests against committed real data:
    //       2024  baseline 7.6507 MAE  ->  enhanced 7.4230  (-2.98%)
    //       2025  baseline 7.4991 MAE  ->  enhanced 7.4708  (-0.38%)
    //     This is the number that matters operationally, and the 2025 gain is slim.
    //
    // (b) ISOLATED FEATURE VALUE — baseline refitted on the same harness as the enhanced fit, so
    //     only the added feature differs:
    //       2024  7.4845 -> 7.3867  (-1.31%)
    //       2025  7.5555 -> 7.4731  (-1.09%)
    //
    // (a) is larger on 2024 and smaller on 2025 than (b) because v1's coefficients come from an
    // earlier harness, so (a) conflates the new feature with that refit. Neither run was tuned to
    // reproduce the other. v1 is preserved byte-for-byte as the comparison baseline and is still
    // used whenever QB evidence is absent.
    //
    // Honest read: the feature is directionally right and fully engaged (QB evidence covered
    // 448/448 games of the 2025 test season), but the production gain on the held-out season is
    // small. It is shipped as an accuracy improvement, not as a breakthrough.
    // ---------------------------------------------------------------------------------------
    public const string EnhancedVersion = "team-points-ols-v2-qb";

    public const decimal QbInterceptWeight = 5.7732m;
    public const decimal QbRollingPointsForWeight = 0.2661m;
    public const decimal QbOpponentPointsAllowedWeight = 0.2058m;
    public const decimal QbHomeFieldWeight = 1.8113m;
    public const decimal QbPriorSeasonWeight = 0.2170m;
    public const decimal QbEpaPerAttemptWeight = 6.4810m;

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

        // One authoritative projection with two evidence levels. QB quality is used when real
        // prior passing work exists; otherwise the untouched v1 baseline runs. The model is never
        // handed a substituted or league-average quarterback.
        var usingQb = features.QuarterbackEpaPerAttempt is not null;

        var points = usingQb
            ? QbInterceptWeight
              + QbRollingPointsForWeight * features.RollingPointsFor
              + QbOpponentPointsAllowedWeight * features.OpponentRollingPointsAllowed
              + QbHomeFieldWeight * (features.IsHome ? 1m : 0m)
              + QbPriorSeasonWeight * features.PriorSeasonPointsFor
              + QbEpaPerAttemptWeight * features.QuarterbackEpaPerAttempt!.Value
            : Intercept
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
            + (usingQb
                ? $"QB {features.QuarterbackEpaPerAttempt!.Value:+0.000;-0.000} EPA/att, "
                : "QB quality unavailable, ")
            + $"{evidence} completed games of history "
            + $"({(usingQb ? EnhancedVersion : Version)})";

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
