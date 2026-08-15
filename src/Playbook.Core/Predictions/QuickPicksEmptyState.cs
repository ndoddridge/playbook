namespace Playbook.Core.Predictions;

/// <summary>
/// Why the Quick Picks board is empty.
///
/// The state is derived from ACTUAL PIPELINE FACTS — how many real lines of each kind arrived,
/// whether the game-market betting gate is open, and whether any prediction survived scoring —
/// never from "the list came back empty". An empty list has several very different causes and
/// reporting the wrong one misleads the user about whether Playbook is working.
/// </summary>
public enum QuickPicksEmptyReason
{
    /// <summary>No sportsbook markets of any kind arrived for this slate.</summary>
    NoMarkets = 0,

    /// <summary>Only player props were offered, and none qualified.</summary>
    NoEligiblePlayerProps = 1,

    /// <summary>
    /// Real game lines arrived, but the game-market betting gate is closed. Nothing was
    /// evaluated for a wager — this is a deliberate product state, not a data problem.
    /// </summary>
    GameMarketBettingDisabled = 2,

    /// <summary>
    /// Markets arrived and betting is enabled, but no prediction cleared the eligibility
    /// thresholds (edge, confidence, data quality).
    /// </summary>
    NoQualifiedPicks = 3,

    /// <summary>Picks exist for the slate but the active UI filter excludes them all.</summary>
    FilteredOut = 4
}

public static class QuickPicksEmptyState
{
    /// <summary>
    /// Classify an empty board from real pipeline state.
    /// </summary>
    /// <param name="gameMarketLines">Real game lines (spread/total/moneyline/team total) on the slate.</param>
    /// <param name="playerPropLines">Real player prop lines on the slate.</param>
    /// <param name="picksBeforeFilter">Qualifying picks produced before the UI filter was applied.</param>
    /// <param name="gameMarketBettingEnabled">
    /// The live state of the game-market betting gate. Passed in rather than inferred, so
    /// "betting is switched off" is never confused with "nothing was good enough".
    /// </param>
    public static QuickPicksEmptyReason Classify(
        int gameMarketLines,
        int playerPropLines,
        int picksBeforeFilter,
        bool gameMarketBettingEnabled)
    {
        // Picks exist; the user's own filter is hiding them. Nothing upstream is at fault.
        if (picksBeforeFilter > 0)
        {
            return QuickPicksEmptyReason.FilteredOut;
        }

        var hasGame = gameMarketLines > 0;
        var hasProps = playerPropLines > 0;

        if (!hasGame && !hasProps)
        {
            return QuickPicksEmptyReason.NoMarkets;
        }

        // Game lines arrived but were never considered for a wager. This takes precedence over
        // prop wording because it is the more actionable fact: the largest available market on
        // the slate was deliberately not evaluated.
        if (hasGame && !gameMarketBettingEnabled)
        {
            return QuickPicksEmptyReason.GameMarketBettingDisabled;
        }

        // Game markets were genuinely evaluated and nothing cleared the bar.
        if (hasGame)
        {
            return QuickPicksEmptyReason.NoQualifiedPicks;
        }

        return QuickPicksEmptyReason.NoEligiblePlayerProps;
    }

    /// <summary>User-facing text. Never blames player props for a game-market situation.</summary>
    public static string Describe(
        QuickPicksEmptyReason reason,
        int gameMarketLines,
        int playerPropLines = 0) => reason switch
    {
        QuickPicksEmptyReason.NoMarkets =>
            "No sportsbook markets are currently available for this slate.",

        QuickPicksEmptyReason.NoEligiblePlayerProps =>
            "No eligible player props for this slate and filter.",

        QuickPicksEmptyReason.GameMarketBettingDisabled =>
            $"Game lines available ({gameMarketLines}), but Playbook betting is currently disabled "
            + "while the game-market model is being validated."
            + (playerPropLines > 0 ? " No eligible player props either." : ""),

        QuickPicksEmptyReason.NoQualifiedPicks =>
            "No qualified picks for this slate.",

        QuickPicksEmptyReason.FilteredOut =>
            "No picks match the current filter.",

        _ => "No picks available for this slate."
    };
}
