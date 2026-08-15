using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// The board must explain itself from real pipeline state, not from "the list came back empty".
///
/// The specific confusion being prevented: real game lines arrive, the betting gate is closed,
/// and the page reports that as though player props were the problem — or as though the model
/// looked at the games and found nothing good. Those are three different situations and the user
/// needs to be able to tell them apart.
/// </summary>
public class QuickPicksEmptyStateTests
{
    // ---------------------------------------------------------------- CASE 1

    [Fact]
    public void GameLinesPresent_BettingDisabled_SaysBettingIsDisabled()
    {
        var reason = QuickPicksEmptyState.Classify(
            gameMarketLines: 21,
            playerPropLines: 0,
            picksBeforeFilter: 0,
            gameMarketBettingEnabled: false);

        Assert.Equal(QuickPicksEmptyReason.GameMarketBettingDisabled, reason);

        var text = QuickPicksEmptyState.Describe(reason, 21);
        Assert.Contains("Game lines available", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("betting is currently disabled", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("being validated", text, StringComparison.OrdinalIgnoreCase);

        // Must not blame player props for a game-market state.
        Assert.DoesNotContain("No eligible props", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameLinesPresent_BettingDisabled_TakesPrecedenceOverProps()
    {
        // Both markets present. The actionable fact is that the larger market was never
        // evaluated, so that leads — but the prop situation is still mentioned.
        var reason = QuickPicksEmptyState.Classify(
            gameMarketLines: 12,
            playerPropLines: 30,
            picksBeforeFilter: 0,
            gameMarketBettingEnabled: false);

        Assert.Equal(QuickPicksEmptyReason.GameMarketBettingDisabled, reason);

        var text = QuickPicksEmptyState.Describe(reason, 12, 30);
        Assert.Contains("betting is currently disabled", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("player props", text, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- CASE 2

    [Fact]
    public void OnlyPlayerProps_NoneEligible_UsesPropWording()
    {
        var reason = QuickPicksEmptyState.Classify(
            gameMarketLines: 0,
            playerPropLines: 40,
            picksBeforeFilter: 0,
            gameMarketBettingEnabled: false);

        Assert.Equal(QuickPicksEmptyReason.NoEligiblePlayerProps, reason);
        Assert.Equal(
            "No eligible player props for this slate and filter.",
            QuickPicksEmptyState.Describe(reason, 0));
    }

    // ---------------------------------------------------------------- CASE 3

    [Fact]
    public void NoMarketsAtAll_SaysMarketsUnavailable()
    {
        var reason = QuickPicksEmptyState.Classify(0, 0, 0, gameMarketBettingEnabled: false);

        Assert.Equal(QuickPicksEmptyReason.NoMarkets, reason);
        Assert.Equal(
            "No sportsbook markets are currently available for this slate.",
            QuickPicksEmptyState.Describe(reason, 0));
    }

    [Fact]
    public void NoMarkets_ReportedEvenWhenBettingIsEnabled()
    {
        // Absence of markets is upstream of the gate; enabling betting must not change it.
        var reason = QuickPicksEmptyState.Classify(0, 0, 0, gameMarketBettingEnabled: true);

        Assert.Equal(QuickPicksEmptyReason.NoMarkets, reason);
    }

    // ---------------------------------------------------------------- CASE 4

    [Fact]
    public void GameLinesPresent_BettingEnabled_NothingQualified_SaysNoQualifiedPicks()
    {
        // This is the state the gate currently hides. It must be reachable and distinct, so the
        // message is correct the day betting is switched on.
        var reason = QuickPicksEmptyState.Classify(
            gameMarketLines: 21,
            playerPropLines: 0,
            picksBeforeFilter: 0,
            gameMarketBettingEnabled: true);

        Assert.Equal(QuickPicksEmptyReason.NoQualifiedPicks, reason);
        Assert.Equal("No qualified picks for this slate.", QuickPicksEmptyState.Describe(reason, 21));
    }

    [Fact]
    public void BettingGate_IsTheOnlyDifferenceBetweenCase1AndCase4()
    {
        // Identical market state; only the gate differs. Proves the classifier reads real
        // pipeline state rather than inferring from the empty prediction list.
        var disabled = QuickPicksEmptyState.Classify(21, 0, 0, gameMarketBettingEnabled: false);
        var enabled = QuickPicksEmptyState.Classify(21, 0, 0, gameMarketBettingEnabled: true);

        Assert.Equal(QuickPicksEmptyReason.GameMarketBettingDisabled, disabled);
        Assert.Equal(QuickPicksEmptyReason.NoQualifiedPicks, enabled);
        Assert.NotEqual(disabled, enabled);
    }

    // ---------------------------------------------------------------- filter

    [Fact]
    public void PicksExistButFilterHidThem_BlamesTheFilter()
    {
        var reason = QuickPicksEmptyState.Classify(
            gameMarketLines: 10,
            playerPropLines: 10,
            picksBeforeFilter: 7,
            gameMarketBettingEnabled: false);

        Assert.Equal(QuickPicksEmptyReason.FilteredOut, reason);
        Assert.Contains("filter", QuickPicksEmptyState.Describe(reason, 10));
    }

    // ---------------------------------------------------------------- live wiring

    [Fact]
    public void TodaysRealSlate_WithTheLiveGate_ProducesTheBettingDisabledMessage()
    {
        // Real slate observed 2026-08-15: 7 preseason games, all carrying h2h + spreads + totals,
        // zero player props. Uses the ACTUAL shipped gate value rather than a hardcoded false, so
        // this test follows the product if the gate is ever opened.
        var reason = QuickPicksEmptyState.Classify(
            gameMarketLines: 21,
            playerPropLines: 0,
            picksBeforeFilter: 0,
            TeamPointsModel.GameMarketBettingEnabled);

        var expected = TeamPointsModel.GameMarketBettingEnabled
            ? QuickPicksEmptyReason.NoQualifiedPicks
            : QuickPicksEmptyReason.GameMarketBettingDisabled;

        Assert.Equal(expected, reason);
    }
}
