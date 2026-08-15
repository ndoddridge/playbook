using Playbook.Core.Players;
using Playbook.Core.Predictions.Models;
using Playbook.Core.Stats.Models;
using Playbook.Infrastructure.Predictions;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// Calibration tests for the team offensive-production aggregate.
///
/// These pin the behaviour the previous milestone report claimed but the code did not have:
/// a bounded health adjustment, a coherent quarterback model, and exclusion of players who are
/// ruled out. They also pin the units problem that keeps game markets withheld.
/// </summary>
public class TeamProductionIndexCalculatorTests
{
    // ---------------------------------------------------------------- health scaling

    [Fact]
    public void HealthMultiplier_NeutralScore_IsExactlyBaseline()
    {
        // HealthScore 50 is documented as neutral, so it must not move the projection at all.
        Assert.Equal(1m, TeamProductionIndexCalculator.HealthMultiplier(50));
    }

    [Fact]
    public void HealthMultiplier_MissingScore_IsBaseline_NotPenalised()
    {
        // Unknown health must never be treated as bad health — that would fabricate a penalty.
        Assert.Equal(1m, TeamProductionIndexCalculator.HealthMultiplier(null));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(85)]
    [InlineData(70)]
    [InlineData(60)]
    [InlineData(50)]
    public void HealthMultiplier_HealthyPlayers_StayNearBaseline(int healthScore)
    {
        // Regression: the previous formula collapsed every score <= 85 to 0.60, suppressing
        // healthy players by 40% and destroying all discrimination between them.
        var multiplier = TeamProductionIndexCalculator.HealthMultiplier(healthScore);

        Assert.InRange(multiplier, 1.00m, 1.05m);
    }

    [Fact]
    public void HealthMultiplier_MildlyCompromised_IsModestReduction()
    {
        var multiplier = TeamProductionIndexCalculator.HealthMultiplier(40);

        Assert.InRange(multiplier, 0.92m, 0.98m);
    }

    [Fact]
    public void HealthMultiplier_SeriouslyCompromised_IsMeaningfulReduction()
    {
        var multiplier = TeamProductionIndexCalculator.HealthMultiplier(10);

        Assert.InRange(multiplier, 0.72m, 0.80m);
    }

    [Fact]
    public void HealthMultiplier_IsMonotonic_AcrossFullRange()
    {
        // Health must be an ordered signal: healthier can never project lower.
        decimal previous = 0m;
        for (var score = 0; score <= 100; score += 5)
        {
            var current = TeamProductionIndexCalculator.HealthMultiplier(score);
            Assert.True(current >= previous, $"HealthScore {score} regressed: {current} < {previous}");
            previous = current;
        }
    }

    [Fact]
    public void HealthMultiplier_NeverDominatesTheProjection()
    {
        // The health factor is an adjustment, not a replacement: it may never halve a projection
        // nor inflate one, at either extreme of the score range.
        var worst = TeamProductionIndexCalculator.HealthMultiplier(0);
        var best = TeamProductionIndexCalculator.HealthMultiplier(100);

        Assert.True(worst >= 0.70m, $"Worst-case health penalty too severe: {worst}");
        Assert.True(best <= 1.05m, $"Best-case health boost too generous: {best}");
    }

    // ---------------------------------------------------------------- quarterback model

    [Fact]
    public void Compute_HealthyQuarterback_IncludesHisOwnProduction()
    {
        // Regression: the previous implementation never added the QB's projection to the team
        // total at all, while still multiplying the rest of the offence by a QB factor.
        var index = TeamProductionIndexCalculator.Compute(
        [
            Qb(20m),
            Skill(Position.RB, 12m),
            Skill(Position.WR, 10m)
        ]);

        Assert.NotNull(index);
        Assert.Equal(20m, index.QuarterbackProduction);
        Assert.Equal(42m, index.FantasyProductionPoints); // 20 + 12 + 10
        Assert.False(index.StartingQuarterbackRuledOut);
    }

    [Fact]
    public void Compute_CompromisedQuarterback_ReducesOnlyViaHisOwnHealth()
    {
        // A limited QB is reflected through his own health-adjusted projection — the skill
        // players are not additionally multiplied, which would double-count him.
        var index = TeamProductionIndexCalculator.Compute(
        [
            Qb(20m, healthScore: 20),
            Skill(Position.RB, 12m),
            Skill(Position.WR, 10m)
        ]);

        Assert.NotNull(index);
        Assert.True(index.QuarterbackProduction < 20m, "Compromised QB should project lower.");
        // Skill production is untouched: 22 of the total comes through unmodified.
        Assert.Equal(22m + index.QuarterbackProduction, index.FantasyProductionPoints);
    }

    [Fact]
    public void Compute_StartingQuarterbackRuledOut_UsesBackupRealProjection()
    {
        // The backup's actual projection is used — not an invented 0.75 replacement multiplier.
        var index = TeamProductionIndexCalculator.Compute(
        [
            Qb(24m, ruledOut: true),
            Qb(11m),
            Skill(Position.RB, 12m)
        ]);

        Assert.NotNull(index);
        Assert.True(index.StartingQuarterbackRuledOut);
        Assert.Equal(11m, index.QuarterbackProduction);
        Assert.Equal(23m, index.FantasyProductionPoints); // 11 backup + 12 RB
    }

    [Fact]
    public void Compute_NoQuarterbackAvailable_ReturnsNull_RatherThanGuessing()
    {
        var index = TeamProductionIndexCalculator.Compute(
        [
            Qb(24m, ruledOut: true),
            Skill(Position.RB, 12m)
        ]);

        Assert.Null(index);
    }

    [Fact]
    public void Compute_NoQuarterbackData_ReturnsNull()
    {
        var index = TeamProductionIndexCalculator.Compute(
        [
            Skill(Position.RB, 12m),
            Skill(Position.WR, 10m)
        ]);

        Assert.Null(index);
    }

    [Fact]
    public void Compute_NoSkillPlayers_ReturnsNull()
    {
        Assert.Null(TeamProductionIndexCalculator.Compute([Qb(20m)]));
    }

    // ---------------------------------------------------------------- ruled-out exclusion

    [Fact]
    public void Compute_RuledOutSkillPlayers_ContributeNothing()
    {
        // Regression: sidelined players previously contributed their full projection to the
        // team total, inflating it by exactly the production that cannot happen.
        var index = TeamProductionIndexCalculator.Compute(
        [
            Qb(20m),
            Skill(Position.RB, 12m),
            Skill(Position.WR, 15m, ruledOut: true)
        ]);

        Assert.NotNull(index);
        Assert.Equal(32m, index.FantasyProductionPoints); // 20 + 12, WR excluded
        Assert.Equal(1, index.SkillPlayersCounted);
        Assert.Equal(1, index.RuledOutCount);
    }

    // ---------------------------------------------------------------- trend

    [Fact]
    public void Compute_TrendAdjustments_AreBoundedAndDirectional()
    {
        var rising = TeamProductionIndexCalculator.Compute(
            [Qb(20m), Skill(Position.WR, 10m, trend: StatisticalTrendSignal.Increasing)])!;
        var falling = TeamProductionIndexCalculator.Compute(
            [Qb(20m), Skill(Position.WR, 10m, trend: StatisticalTrendSignal.Decreasing)])!;
        var flat = TeamProductionIndexCalculator.Compute(
            [Qb(20m), Skill(Position.WR, 10m, trend: StatisticalTrendSignal.Stable)])!;

        Assert.True(rising.FantasyProductionPoints > flat.FantasyProductionPoints);
        Assert.True(falling.FantasyProductionPoints < flat.FantasyProductionPoints);
        // Bounded: a trend may not move the aggregate by more than ~5% of the affected player.
        Assert.InRange(rising.FantasyProductionPoints - flat.FantasyProductionPoints, 0.1m, 0.6m);
    }

    // ---------------------------------------------------------------- units / scale

    [Fact]
    public void Compute_AggregateIsFantasyProduction_NotNflPoints()
    {
        // A realistic NFL offence: QB ~19, RB corps ~19, WR corps ~30, TE ~9 PPR points.
        // The aggregate lands near 75 — roughly 3.5x a real team score of ~22. This test
        // documents the units gap that keeps game markets withheld; if a future calibration
        // lands, it must reconcile these two numbers rather than compare them directly.
        var index = TeamProductionIndexCalculator.Compute(
        [
            Qb(19m),
            Skill(Position.RB, 12m), Skill(Position.RB, 7m),
            Skill(Position.WR, 14m), Skill(Position.WR, 10m), Skill(Position.WR, 6m),
            Skill(Position.TE, 9m)
        ]);

        Assert.NotNull(index);
        Assert.Equal(77m, index.FantasyProductionPoints);

        // The decisive assertion: this is NOT in the range of an NFL team score.
        Assert.True(
            index.FantasyProductionPoints > 45m,
            "Aggregate fantasy production must not be mistaken for an NFL team score.");
    }

    // ---------------------------------------------------------------- spread convention

    [Theory]
    [InlineData(-6.5, 6.5)]   // home favoured by 6.5
    [InlineData(3.5, -3.5)]   // home underdog by 3.5
    [InlineData(0, 0)]
    public void MarketImpliedHomeMargin_InvertsTheBookConvention(decimal line, decimal expectedMargin)
    {
        // TheOddsAPI publishes a home favourite as a negative number; a projected margin is
        // positive when home is better. Edge maths must convert before subtracting.
        Assert.Equal(expectedMargin, GameMarketProjector.MarketImpliedHomeMargin(line));
    }

    [Fact]
    public void MarketImpliedHomeMargin_ProducesSmallEdge_WhenProjectionAgreesWithMarket()
    {
        // Home projected to win by 8, market has home -6.5. True edge is 1.5 points.
        // Subtracting the raw line instead would give 14.5 — nearly ten times too large.
        const decimal projectedHomeMargin = 8m;
        const decimal homeSpreadLine = -6.5m;

        var correctEdge = projectedHomeMargin - GameMarketProjector.MarketImpliedHomeMargin(homeSpreadLine);
        var naiveEdge = projectedHomeMargin - homeSpreadLine;

        Assert.Equal(1.5m, correctEdge);
        Assert.Equal(14.5m, naiveEdge);
    }

    // ---------------------------------------------------------------- helpers

    private static TeamPlayerProductionInput Qb(
        decimal points,
        bool ruledOut = false,
        int? healthScore = null) => new()
        {
            Position = Position.QB,
            ProjectedFantasyPoints = points,
            IsRuledOut = ruledOut,
            HealthScore = healthScore,
            Trend = StatisticalTrendSignal.Unknown
        };

    private static TeamPlayerProductionInput Skill(
        Position position,
        decimal points,
        bool ruledOut = false,
        int? healthScore = null,
        StatisticalTrendSignal trend = StatisticalTrendSignal.Unknown) => new()
        {
            Position = position,
            ProjectedFantasyPoints = points,
            IsRuledOut = ruledOut,
            HealthScore = healthScore,
            Trend = trend
        };
}
