namespace Playbook.Core.Predictions;

/// <summary>
/// Why the Quick Picks board is empty.
///
/// "No eligible props" was misleading whenever real game lines existed: it implied Playbook had
/// looked at player props and found nothing, when in fact it had declined to evaluate the game
/// markets at all. These states keep the distinction between "no market exists", "a market
/// exists but Playbook cannot form a defensible opinion", and "picks exist but the filter hid
/// them" visible to the user.
/// </summary>
public enum QuickPicksEmptyReason
{
    /// <summary>No sportsbook markets of any kind arrived for this slate.</summary>
    NoMarkets = 0,

    /// <summary>Only player props were offered, and none qualified.</summary>
    NoEligiblePlayerProps = 1,

    /// <summary>Real game lines exist, but Playbook cannot responsibly evaluate them yet.</summary>
    GameLinesNotEvaluable = 2,

    /// <summary>Both kinds of market exist; neither produced a qualifying pick.</summary>
    NeitherMarketQualified = 3,

    /// <summary>Picks exist for the slate but the active filter excludes them all.</summary>
    FilteredOut = 4
}

public static class QuickPicksEmptyState
{
    /// <summary>
    /// Classify an empty board from what the slate actually contained.
    /// </summary>
    /// <param name="gameMarketLines">Real game lines (spread/total/moneyline/team total) on the slate.</param>
    /// <param name="playerPropLines">Real player prop lines on the slate.</param>
    /// <param name="picksBeforeFilter">Qualifying picks produced before the UI filter was applied.</param>
    public static QuickPicksEmptyReason Classify(
        int gameMarketLines,
        int playerPropLines,
        int picksBeforeFilter)
    {
        if (picksBeforeFilter > 0)
        {
            return QuickPicksEmptyReason.FilteredOut;
        }

        var hasGame = gameMarketLines > 0;
        var hasProps = playerPropLines > 0;

        return (hasGame, hasProps) switch
        {
            (false, false) => QuickPicksEmptyReason.NoMarkets,
            (true, false) => QuickPicksEmptyReason.GameLinesNotEvaluable,
            (false, true) => QuickPicksEmptyReason.NoEligiblePlayerProps,
            (true, true) => QuickPicksEmptyReason.NeitherMarketQualified
        };
    }

    /// <summary>User-facing text. Never claims props were the only thing on offer.</summary>
    public static string Describe(QuickPicksEmptyReason reason, int gameMarketLines) => reason switch
    {
        QuickPicksEmptyReason.NoMarkets =>
            "No sportsbook markets are available for this slate.",

        QuickPicksEmptyReason.NoEligiblePlayerProps =>
            "No eligible player props for this slate and filter.",

        QuickPicksEmptyReason.GameLinesNotEvaluable =>
            $"{gameMarketLines} real game line(s) are available for this slate, but Playbook does not "
            + "currently have enough information to evaluate them responsibly. No player props are offered.",

        QuickPicksEmptyReason.NeitherMarketQualified =>
            $"No eligible player props. {gameMarketLines} real game line(s) are available, but Playbook "
            + "does not currently have enough information to evaluate them responsibly.",

        QuickPicksEmptyReason.FilteredOut =>
            "No picks match the current filter.",

        _ => "No picks available for this slate."
    };
}
