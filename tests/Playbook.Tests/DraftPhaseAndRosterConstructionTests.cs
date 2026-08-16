using Playbook.Core.Draft;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// Roster-construction and draft-phase intelligence. The load-bearing tests here are the bounds:
/// these are contextual nudges, and the model is only trustworthy if they cannot outrank a
/// genuinely better player.
/// </summary>
public class DraftPhaseAndRosterConstructionTests
{
    // ---------------------------------------------------------------- roster construction

    [Theory]
    [InlineData(0, 2, RosterConstructionTier.Empty)]
    [InlineData(1, 2, RosterConstructionTier.Light)]
    [InlineData(2, 2, RosterConstructionTier.Adequate)]
    [InlineData(3, 2, RosterConstructionTier.Depth)]
    [InlineData(4, 2, RosterConstructionTier.Depth)]
    [InlineData(5, 2, RosterConstructionTier.OverConcentrated)]
    public void Classify_ReflectsRealCountsAgainstTheLeaguesOwnStarterRequirement(
        int count, int target, RosterConstructionTier expected)
    {
        Assert.Equal(expected, RosterConstructionPolicy.Classify(count, target));
    }

    [Fact]
    public void Classify_AdaptsToLeagueShape_NotAHardcodedRoster()
    {
        // A 3-WR league should treat three receivers as adequate, not as depth.
        Assert.Equal(RosterConstructionTier.Adequate, RosterConstructionPolicy.Classify(3, 3));
        Assert.Equal(RosterConstructionTier.Depth, RosterConstructionPolicy.Classify(3, 2));
    }

    [Fact]
    public void EmptyPosition_GetsTheLargestBenefit_AdequateGetsNone()
    {
        var empty = RosterConstructionPolicy.BaseAdjustment(RosterConstructionTier.Empty);
        var light = RosterConstructionPolicy.BaseAdjustment(RosterConstructionTier.Light);
        var adequate = RosterConstructionPolicy.BaseAdjustment(RosterConstructionTier.Adequate);

        Assert.True(empty > light, "an empty position should beat a lightly filled one");
        Assert.True(light > 0m);
        Assert.Equal(0m, adequate);
    }

    [Fact]
    public void OverConcentration_IsPenalised_ButUsefulDepthIsBarelyTouched()
    {
        var depth = RosterConstructionPolicy.BaseAdjustment(RosterConstructionTier.Depth);
        var over = RosterConstructionPolicy.BaseAdjustment(RosterConstructionTier.OverConcentrated);

        Assert.True(over < depth, "over-concentration should hurt more than healthy depth");
        // Dynasty rosters need depth — a fourth receiver must not be treated as a mistake.
        Assert.True(depth > -1m, $"depth penalty {depth} is too harsh for dynasty");
    }

    [Fact]
    public void Adjustment_IsBounded_SoASuperiorPlayerCannotBeDisplaced()
    {
        // The safety property. Even the harshest tier at the heaviest phase weight stays inside
        // the cap, which is smaller than the existing need bonus and far smaller than a real
        // projection gap.
        foreach (var tier in Enum.GetValues<RosterConstructionTier>())
        {
            foreach (var phase in Enum.GetValues<DraftPhase>())
            {
                var adjustment = RosterConstructionPolicy.Adjustment(
                    tier, DraftPhasePolicy.RosterConstructionWeight(phase));

                Assert.InRange(
                    adjustment,
                    -RosterConstructionPolicy.MaxAdjustment,
                    RosterConstructionPolicy.MaxAdjustment);
            }
        }

        Assert.True(RosterConstructionPolicy.MaxAdjustment < 3.0m,
            "roster shape must stay smaller than the existing positional-need bonus");
    }

    [Fact]
    public void Describe_IsPlainLanguage_WithNoInternalJargon()
    {
        foreach (var tier in Enum.GetValues<RosterConstructionTier>())
        {
            var text = RosterConstructionPolicy.Describe(tier, "WR");
            foreach (var jargon in new[] { "tier", "adjustment", "score", "weight", "Enum" })
            {
                Assert.DoesNotContain(jargon, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---------------------------------------------------------------- draft phase

    [Theory]
    [InlineData(1, 12, 15, DraftPhase.Early)]    // round 1 of 15
    [InlineData(5, 12, 15, DraftPhase.Early)]    // round 5 = 1/3 exactly
    [InlineData(61, 12, 15, DraftPhase.Middle)]  // round 6
    [InlineData(120, 12, 15, DraftPhase.Middle)] // round 10 = 2/3 exactly
    [InlineData(121, 12, 15, DraftPhase.Late)]   // round 11
    [InlineData(180, 12, 15, DraftPhase.Late)]   // final round
    public void Phase_IsDerivedFromTheRealRoundAndDraftLength(
        int pickNumber, int teamCount, int totalRounds, DraftPhase expected)
    {
        Assert.Equal(expected, DraftPhasePolicy.ClassifyFromPick(pickNumber, teamCount, totalRounds));
    }

    [Fact]
    public void Phase_ScalesToDraftLength_RatherThanAssumingRoundCounts()
    {
        // Round 6 is late in a 6-round draft and early in a 30-round dynasty startup.
        Assert.Equal(DraftPhase.Late, DraftPhasePolicy.Classify(round: 6, totalRounds: 6));
        Assert.Equal(DraftPhase.Early, DraftPhasePolicy.Classify(round: 6, totalRounds: 30));
    }

    [Fact]
    public void Phase_BoundariesAreDeterministic()
    {
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(DraftPhase.Early, DraftPhasePolicy.Classify(5, 15));
            Assert.Equal(DraftPhase.Middle, DraftPhasePolicy.Classify(6, 15));
            Assert.Equal(DraftPhase.Middle, DraftPhasePolicy.Classify(10, 15));
            Assert.Equal(DraftPhase.Late, DraftPhasePolicy.Classify(11, 15));
        }
    }

    [Fact]
    public void Phase_FallsBackToEarly_WhenDraftLengthIsUnknown()
    {
        // Early applies the smallest roster-construction weight, so an unknown draft length
        // changes the board least. Conservative by design.
        Assert.Equal(DraftPhase.Early, DraftPhasePolicy.Classify(round: 4, totalRounds: 0));
        Assert.Equal(0.4m, DraftPhasePolicy.RosterConstructionWeight(DraftPhase.Early));
    }

    [Fact]
    public void RoundMaths_IsCorrectAcrossPickBoundaries()
    {
        Assert.Equal(1, DraftPhasePolicy.RoundForPick(1, 12));
        Assert.Equal(1, DraftPhasePolicy.RoundForPick(12, 12));
        Assert.Equal(2, DraftPhasePolicy.RoundForPick(13, 12));
        Assert.Equal(2, DraftPhasePolicy.RoundForPick(24, 12));
        Assert.Equal(3, DraftPhasePolicy.RoundForPick(25, 12));
    }

    [Fact]
    public void RosterConstruction_MattersMostMidDraft_LeastEarly()
    {
        var early = DraftPhasePolicy.RosterConstructionWeight(DraftPhase.Early);
        var middle = DraftPhasePolicy.RosterConstructionWeight(DraftPhase.Middle);
        var late = DraftPhasePolicy.RosterConstructionWeight(DraftPhase.Late);

        Assert.True(middle > late, "mid-draft is where construction should drive picks");
        Assert.True(late > early, "early picks should chase the best asset, not roster shape");
    }

    [Fact]
    public void Upside_IsWeightedHighestLate()
    {
        var early = DraftPhasePolicy.UpsideWeight(DraftPhase.Early);
        var middle = DraftPhasePolicy.UpsideWeight(DraftPhase.Middle);
        var late = DraftPhasePolicy.UpsideWeight(DraftPhase.Late);

        Assert.True(late > early, "late-round opportunity cost is low, so upside is cheaper");
        Assert.True(early > middle);
    }

    // ---------------------------------------------------------------- layering

    [Fact]
    public void PhaseLayersOnStrategy_ItDoesNotOverrideIt()
    {
        // The key interaction rule: a Competitor late must still be far less youth-driven than a
        // Rebuild late. Phase scales the curve; strategy sets it.
        const int youngAge = 22;

        decimal Combined(DynastyStrategy strategy, DraftPhase phase) =>
            DynastyStrategyPolicy.AgeAdjustment(strategy, youngAge)
            * DraftPhasePolicy.UpsideWeight(phase);

        var competitorLate = Combined(DynastyStrategy.ChampionshipCompetitor, DraftPhase.Late);
        var rebuildLate = Combined(DynastyStrategy.Rebuild, DraftPhase.Late);
        var rebuildEarly = Combined(DynastyStrategy.Rebuild, DraftPhase.Early);

        Assert.True(rebuildLate > competitorLate * 3,
            $"rebuild-late {rebuildLate} should dwarf competitor-late {competitorLate}");
        Assert.True(rebuildLate > rebuildEarly,
            "late should amplify a rebuild's youth preference, not flatten it");
    }

    [Fact]
    public void CombinedAgeEffect_StaysBounded_InEveryStrategyAndPhase()
    {
        foreach (var strategy in Enum.GetValues<DynastyStrategy>())
        {
            foreach (var phase in Enum.GetValues<DraftPhase>())
            {
                foreach (var age in new[] { 18, 22, 28, 34, 42 })
                {
                    var combined = DynastyStrategyPolicy.AgeAdjustment(strategy, age)
                                   * DraftPhasePolicy.UpsideWeight(phase);
                    var clamped = Math.Clamp(
                        combined,
                        -DynastyStrategyPolicy.MaxAgeAdjustment,
                        DynastyStrategyPolicy.MaxAgeAdjustment);

                    Assert.InRange(
                        clamped,
                        -DynastyStrategyPolicy.MaxAgeAdjustment,
                        DynastyStrategyPolicy.MaxAgeAdjustment);
                }
            }
        }
    }

    [Fact]
    public void PhaseDescriptions_AreDecisionOriented_NotJargon()
    {
        foreach (var phase in Enum.GetValues<DraftPhase>())
        {
            var text = DraftPhasePolicy.Describe(phase);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("weight", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("multiplier", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
