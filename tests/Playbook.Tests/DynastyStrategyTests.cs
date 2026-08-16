using Playbook.Core.Draft;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// The dynasty strategy selector must actually change the board, not just the UI. These pin the
/// behavioural differences between the three postures, and the guardrail that stops an age curve
/// from overturning a real production gap.
/// </summary>
public class DynastyStrategyTests
{
    // ---------------------------------------------------------------- postures differ

    [Fact]
    public void YoungPlayer_IsRewardedMostByRebuild_LeastByCompetitor()
    {
        const int age = 22;

        var competitor = DynastyStrategyPolicy.AgeAdjustment(DynastyStrategy.ChampionshipCompetitor, age);
        var hybrid = DynastyStrategyPolicy.AgeAdjustment(DynastyStrategy.Hybrid, age);
        var rebuild = DynastyStrategyPolicy.AgeAdjustment(DynastyStrategy.Rebuild, age);

        Assert.True(rebuild > hybrid, $"rebuild {rebuild} should exceed hybrid {hybrid}");
        Assert.True(hybrid > competitor, $"hybrid {hybrid} should exceed competitor {competitor}");
        Assert.True(competitor > 0, "a 22-year-old should never be penalised for youth");
    }

    [Fact]
    public void AgingPlayer_IsPunishedMostByRebuild_BarelyByCompetitor()
    {
        const int age = 33;

        var competitor = DynastyStrategyPolicy.AgeAdjustment(DynastyStrategy.ChampionshipCompetitor, age);
        var hybrid = DynastyStrategyPolicy.AgeAdjustment(DynastyStrategy.Hybrid, age);
        var rebuild = DynastyStrategyPolicy.AgeAdjustment(DynastyStrategy.Rebuild, age);

        Assert.True(rebuild < hybrid, $"rebuild {rebuild} should be harsher than hybrid {hybrid}");
        Assert.True(hybrid < competitor, $"hybrid {hybrid} should be harsher than competitor {competitor}");

        // A contender should be able to draft a productive 33-year-old without much friction.
        Assert.True(competitor > -1m, $"competitor penalty {competitor} is too harsh for a win-now team");
    }

    [Fact]
    public void PrimeAge_IsNeutral_InEveryStrategy()
    {
        foreach (var strategy in AllStrategies())
        {
            Assert.Equal(0m, DynastyStrategyPolicy.AgeAdjustment(strategy, 28));
        }
    }

    [Fact]
    public void NeedWeighting_MattersMostToContenders_LeastToRebuilds()
    {
        var competitor = DynastyStrategyPolicy.NeedWeightMultiplier(DynastyStrategy.ChampionshipCompetitor);
        var hybrid = DynastyStrategyPolicy.NeedWeightMultiplier(DynastyStrategy.Hybrid);
        var rebuild = DynastyStrategyPolicy.NeedWeightMultiplier(DynastyStrategy.Rebuild);

        Assert.True(competitor > hybrid);
        Assert.True(hybrid > rebuild);
        Assert.True(rebuild > 0m, "even a rebuild should not ignore roster construction entirely");
    }

    // ---------------------------------------------------------------- guardrail

    [Fact]
    public void AgeAdjustment_IsBounded_SoItCannotOverturnProduction()
    {
        // The core safety property: an extreme age in the harshest posture still cannot swing the
        // score more than the cap, so a genuinely better player stays ahead of a young non-starter.
        foreach (var strategy in AllStrategies())
        {
            foreach (var age in new[] { 18, 20, 25, 30, 36, 44 })
            {
                var adjustment = DynastyStrategyPolicy.AgeAdjustment(strategy, age);
                Assert.InRange(
                    adjustment,
                    -DynastyStrategyPolicy.MaxAgeAdjustment,
                    DynastyStrategyPolicy.MaxAgeAdjustment);
            }
        }
    }

    [Fact]
    public void AgeAdjustment_IsMonotonic_YoungerIsNeverWorse()
    {
        foreach (var strategy in AllStrategies())
        {
            var previous = decimal.MinValue;
            for (var age = 44; age >= 18; age--)
            {
                var current = DynastyStrategyPolicy.AgeAdjustment(strategy, age);
                Assert.True(current >= previous, $"{strategy}: age {age} regressed ({current} < {previous})");
                previous = current;
            }
        }
    }

    [Fact]
    public void EveryStrategy_HasDisplayNameAndDescription()
    {
        foreach (var strategy in AllStrategies())
        {
            Assert.False(string.IsNullOrWhiteSpace(DynastyStrategyPolicy.DisplayName(strategy)));
            Assert.False(string.IsNullOrWhiteSpace(DynastyStrategyPolicy.Description(strategy)));
        }
    }

    private static IEnumerable<DynastyStrategy> AllStrategies() =>
    [
        DynastyStrategy.ChampionshipCompetitor,
        DynastyStrategy.Hybrid,
        DynastyStrategy.Rebuild
    ];
}
