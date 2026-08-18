using Playbook.Core.Draft;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// Dynamic tier/cliff detection — tiers must come from real gaps in the remaining pool's
/// projections, never from dividing rank into equal buckets (Part VII of the Draft Assistant
/// intelligence overhaul).
/// </summary>
public class PositionalTierPolicyTests
{
    [Fact]
    public void AssignTiers_Detects_A_Real_Cliff_Between_Tight_Clusters()
    {
        // The playbook's own worked example: a tight top cluster, a real cliff, then another
        // tight cluster.
        var projections = new decimal[] { 16.5m, 16.1m, 15.9m, 15.6m, 15.4m, 12.3m, 11.9m, 11.7m };

        var tiers = PositionalTierPolicy.AssignTiers(projections);

        Assert.Equal([1, 1, 1, 1, 1, 2, 2, 2], tiers);
    }

    [Fact]
    public void AssignTiers_Everything_One_Tier_When_Values_Are_Flat()
    {
        var projections = new decimal[] { 10.0m, 9.9m, 9.8m, 9.7m, 9.6m };

        var tiers = PositionalTierPolicy.AssignTiers(projections);

        Assert.All(tiers, t => Assert.Equal(1, t));
    }

    [Fact]
    public void AssignTiers_Single_Player_Is_Tier_One()
    {
        Assert.Equal([1], PositionalTierPolicy.AssignTiers([12.3m]));
    }

    [Fact]
    public void AssignTiers_Empty_Pool_Returns_Empty()
    {
        Assert.Empty(PositionalTierPolicy.AssignTiers([]));
    }

    [Fact]
    public void AssignTiers_Multiple_Cliffs_Produce_Multiple_Tiers()
    {
        var projections = new decimal[] { 30m, 29.5m, 20m, 19.6m, 19.4m, 8m, 7.8m };

        var tiers = PositionalTierPolicy.AssignTiers(projections);

        Assert.Equal([1, 1, 2, 2, 2, 3, 3], tiers);
    }

    [Fact]
    public void AssignTiers_Never_Manufactures_A_Cliff_Below_The_Absolute_Floor()
    {
        // Gaps of 0.3 between every player — real but tiny. Even though they're all "different"
        // from each other, none should individually be flagged as a cliff.
        var projections = Enumerable.Range(0, 10).Select(i => 20m - i * 0.3m).ToArray();

        var tiers = PositionalTierPolicy.AssignTiers(projections);

        Assert.All(tiers, t => Assert.Equal(1, t));
    }

    [Fact]
    public void BuildTierInfo_Flags_Last_Player_In_Tier_Before_The_Drop()
    {
        var projections = new decimal[] { 20m, 19.8m, 19.7m, 10m, 9.8m };

        var info = PositionalTierPolicy.BuildTierInfo(projections);

        Assert.False(info[0].IsLastInTier);
        Assert.False(info[1].IsLastInTier);
        Assert.True(info[2].IsLastInTier, "the player right before the cliff must be flagged");
        Assert.False(info[3].IsLastInTier);
        Assert.True(info[4].IsLastInTier, "the last player overall is always last-in-tier");
    }

    [Fact]
    public void BuildTierInfo_Reports_Correct_PlayersInTier_Count()
    {
        var projections = new decimal[] { 20m, 19.8m, 19.7m, 10m, 9.8m };

        var info = PositionalTierPolicy.BuildTierInfo(projections);

        Assert.Equal(3, info[0].PlayersInTier);
        Assert.Equal(3, info[1].PlayersInTier);
        Assert.Equal(3, info[2].PlayersInTier);
        Assert.Equal(2, info[3].PlayersInTier);
        Assert.Equal(2, info[4].PlayersInTier);
    }
}
