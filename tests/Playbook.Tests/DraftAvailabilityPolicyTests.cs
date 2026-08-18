using Playbook.Core.Draft;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// Pick timing / expected availability (Part V) — estimated from THIS draft's own observed
/// positional pace, never from a fabricated market ADP.
/// </summary>
public class DraftAvailabilityPolicyTests
{
    [Fact]
    public void ObservedPositionRate_Unknown_Before_Enough_Picks_Have_Happened()
    {
        var rate = DraftAvailabilityPolicy.ObservedPositionRate(
            picksAtPositionSoFar: 1, totalPicksSoFar: DraftAvailabilityPolicy.MinimumPicksForRate - 1);

        Assert.True(rate < 0m, "should be the unknown sentinel before the minimum sample size");
    }

    [Fact]
    public void ObservedPositionRate_Computes_Real_Ratio_Once_Enough_History_Exists()
    {
        var rate = DraftAvailabilityPolicy.ObservedPositionRate(picksAtPositionSoFar: 3, totalPicksSoFar: 12);

        Assert.Equal(0.25m, rate);
    }

    [Fact]
    public void Classify_Unknown_When_Rate_Is_The_Sentinel()
    {
        var risk = DraftAvailabilityPolicy.Classify(observedPositionRate: -1m, picksUntilNextUserPick: 5, positionalRank: 2);

        Assert.Equal(AvailabilityRisk.Unknown, risk);
    }

    [Fact]
    public void Classify_Safe_When_It_Is_Already_The_Users_Turn()
    {
        var risk = DraftAvailabilityPolicy.Classify(observedPositionRate: 0.5m, picksUntilNextUserPick: 0, positionalRank: 1);

        Assert.Equal(AvailabilityRisk.Safe, risk);
    }

    [Fact]
    public void Classify_AtRisk_When_The_Positional_Run_Rate_Would_Consume_This_Players_Tier()
    {
        // A heavy RB run (0.5 of all picks) with 10 picks until the next turn implies ~5 RBs gone
        // — enough to reach a player ranked 4th at the position.
        var risk = DraftAvailabilityPolicy.Classify(observedPositionRate: 0.5m, picksUntilNextUserPick: 10, positionalRank: 4);

        Assert.Equal(AvailabilityRisk.AtRisk, risk);
    }

    [Fact]
    public void Classify_Safe_When_The_Player_Is_Far_Deeper_Than_The_Expected_Run()
    {
        // Same rate and window, but this player is 40th at the position — nowhere near being
        // reached by an expected ~5 picks at the position.
        var risk = DraftAvailabilityPolicy.Classify(observedPositionRate: 0.5m, picksUntilNextUserPick: 10, positionalRank: 40);

        Assert.Equal(AvailabilityRisk.Safe, risk);
    }

    [Fact]
    public void Classify_MoreUrgent_The_Fewer_Picks_Remain_Before_The_Users_Turn()
    {
        // Holding rate and rank fixed, fewer picks until the user's turn should never make a
        // player look MORE at risk than more picks would.
        var closeRisk = DraftAvailabilityPolicy.Classify(observedPositionRate: 0.3m, picksUntilNextUserPick: 2, positionalRank: 5);
        var farRisk = DraftAvailabilityPolicy.Classify(observedPositionRate: 0.3m, picksUntilNextUserPick: 20, positionalRank: 5);

        Assert.Equal(AvailabilityRisk.Safe, closeRisk);
        Assert.Equal(AvailabilityRisk.AtRisk, farRisk);
    }

    [Fact]
    public void Describe_Never_Leaks_Raw_Rates_Or_Ranks_Into_The_Copy()
    {
        foreach (var risk in Enum.GetValues<AvailabilityRisk>())
        {
            var text = DraftAvailabilityPolicy.Describe(risk, picksUntilNextUserPick: 5, positionLabel: "WR");
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("rate", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rank", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
