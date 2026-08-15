using Playbook.Core.Predictions;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// The board must explain itself honestly. The specific failure being pinned here: real
/// preseason game lines arrive from the provider, Playbook declines to evaluate them, and the
/// UI previously reported that as "No eligible props for this slate and filter" — blaming a
/// market that was never the issue.
/// </summary>
public class QuickPicksEmptyStateTests
{
    [Fact]
    public void GameLinesPresent_NoProps_DoesNotBlamePlayerProps()
    {
        var reason = QuickPicksEmptyState.Classify(
            gameMarketLines: 21, playerPropLines: 0, picksBeforeFilter: 0);

        Assert.Equal(QuickPicksEmptyReason.GameLinesNotEvaluable, reason);

        var text = QuickPicksEmptyState.Describe(reason, 21);
        Assert.Contains("game line", text, StringComparison.OrdinalIgnoreCase);
        // The reason given must be the real one: the model has no demonstrated edge over the
        // closing line, not that player props were unavailable.
        Assert.Contains("no demonstrated edge", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No eligible props", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoMarketsAtAll_SaysSo()
    {
        var reason = QuickPicksEmptyState.Classify(0, 0, 0);

        Assert.Equal(QuickPicksEmptyReason.NoMarkets, reason);
        Assert.Contains("No sportsbook markets", QuickPicksEmptyState.Describe(reason, 0));
    }

    [Fact]
    public void OnlyProps_NoneEligible_KeepsThePropWording()
    {
        var reason = QuickPicksEmptyState.Classify(0, 40, 0);

        Assert.Equal(QuickPicksEmptyReason.NoEligiblePlayerProps, reason);
        Assert.Contains("player props", QuickPicksEmptyState.Describe(reason, 0));
    }

    [Fact]
    public void BothMarketsPresent_NeitherQualified_MentionsBoth()
    {
        var reason = QuickPicksEmptyState.Classify(12, 30, 0);

        Assert.Equal(QuickPicksEmptyReason.NeitherMarketQualified, reason);

        var text = QuickPicksEmptyState.Describe(reason, 12);
        Assert.Contains("player props", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("game line", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PicksExistButFilterHidThem_BlamesTheFilter_NotTheMarket()
    {
        var reason = QuickPicksEmptyState.Classify(
            gameMarketLines: 10, playerPropLines: 10, picksBeforeFilter: 7);

        Assert.Equal(QuickPicksEmptyReason.FilteredOut, reason);
        Assert.Contains("filter", QuickPicksEmptyState.Describe(reason, 10));
    }

    [Fact]
    public void TodaysRealPreseasonSlate_ProducesTheGameLineMessage()
    {
        // Real slate observed 2026-08-15: 7 preseason games, every one carrying h2h + spreads +
        // totals from 7-9 books, and zero player props. That is 21 game lines at one book.
        var reason = QuickPicksEmptyState.Classify(
            gameMarketLines: 21, playerPropLines: 0, picksBeforeFilter: 0);

        Assert.Equal(QuickPicksEmptyReason.GameLinesNotEvaluable, reason);
    }
}
