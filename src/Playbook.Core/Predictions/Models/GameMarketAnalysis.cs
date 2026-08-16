namespace Playbook.Core.Predictions.Models;

/// <summary>
/// What Playbook is willing to say about one game market.
///
/// Deliberately separates "here is the model's view" from "here is a wager". While the
/// game-market betting gate is closed, the strongest available status is
/// <see cref="AnalysisOnly"/> — never a recommendation.
/// </summary>
public enum GameMarketAnalysisStatus
{
    /// <summary>No projection exists. Playbook has nothing to say beyond the market's own number.</summary>
    NoPlay = 0,

    /// <summary>A real projection exists, but betting is disabled. Shown for insight, not action.</summary>
    AnalysisOnly = 1,

    /// <summary>
    /// A real projection exists and betting is enabled. Unreachable while
    /// TeamPointsModel.GameMarketBettingEnabled is false; modelled so the path is defined and
    /// tested ahead of the gate ever opening.
    /// </summary>
    BetEligible = 2
}

/// <summary>
/// One game's real sportsbook lines alongside Playbook's real model view, or an honest statement
/// that no view exists. Every numeric field is either a genuine market number, a genuine model
/// output, or null. Nothing here is inferred to fill a gap.
/// </summary>
public sealed record GameMarketAnalysis
{
    public required string AwayTeam { get; init; }

    public required string HomeTeam { get; init; }

    /// <summary>e.g. "PHI @ BAL".</summary>
    public string Matchup => $"{AwayTeam} @ {HomeTeam}";

    public required DateTimeOffset CommenceTime { get; init; }

    /// <summary>Real home spread from the book, in the book's convention (negative = home favoured).</summary>
    public decimal? SpreadLine { get; init; }

    /// <summary>Real game total from the book.</summary>
    public decimal? TotalLine { get; init; }

    public string? Bookmaker { get; init; }

    /// <summary>Playbook's projected home margin (positive = home better). Null when unavailable.</summary>
    public decimal? ProjectedHomeMargin { get; init; }

    /// <summary>Playbook's projected combined score. Null when unavailable.</summary>
    public decimal? ProjectedTotal { get; init; }

    public required GameMarketAnalysisStatus Status { get; init; }

    /// <summary>Plain-language explanation of the status. Always populated.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Difference between Playbook's projected margin and the margin the market implies.
    /// Null unless both exist. Presented as disagreement, never as a recommended side.
    /// </summary>
    public decimal? MarginDisagreement =>
        ProjectedHomeMargin is { } projected && SpreadLine is { } line
            ? Math.Round(projected - (-line), 1, MidpointRounding.AwayFromZero)
            : null;

    /// <summary>Difference between Playbook's projected total and the market total.</summary>
    public decimal? TotalDisagreement =>
        ProjectedTotal is { } projected && TotalLine is { } line
            ? Math.Round(projected - line, 1, MidpointRounding.AwayFromZero)
            : null;

    public bool HasProjection => ProjectedHomeMargin is not null || ProjectedTotal is not null;
}

/// <summary>
/// Status and label rules for game-market analysis.
///
/// The single invariant enforced here: while betting is disabled, no label may contain the word
/// "BET". Card rendering goes through <see cref="StatusLabel"/> so that rule lives in one place
/// and is directly testable, rather than being scattered across markup.
/// </summary>
public static class GameMarketAnalysisPolicy
{
    public const string NoPlayLabel = "NO PLAY";
    public const string AnalysisOnlyLabel = "ANALYSIS ONLY";
    public const string BetEligibleLabel = "BET ELIGIBLE";

    /// <summary>
    /// Resolve status from real state: does a projection exist, and is the gate open?
    /// A missing projection is always NO PLAY regardless of the gate.
    /// </summary>
    public static GameMarketAnalysisStatus ResolveStatus(bool hasProjection, bool bettingEnabled)
    {
        if (!hasProjection)
        {
            return GameMarketAnalysisStatus.NoPlay;
        }

        return bettingEnabled
            ? GameMarketAnalysisStatus.BetEligible
            : GameMarketAnalysisStatus.AnalysisOnly;
    }

    public static string StatusLabel(GameMarketAnalysisStatus status) => status switch
    {
        GameMarketAnalysisStatus.NoPlay => NoPlayLabel,
        GameMarketAnalysisStatus.AnalysisOnly => AnalysisOnlyLabel,
        GameMarketAnalysisStatus.BetEligible => BetEligibleLabel,
        _ => NoPlayLabel
    };

    /// <summary>Reason text for a projection that exists but cannot be acted on.</summary>
    public const string BettingDisabledReason =
        "Betting disabled while game-market model validation continues";
}
