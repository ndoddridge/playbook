using Playbook.Core.Players;
using Playbook.Core.Stats.Models;

namespace Playbook.Core.Predictions.Models;

/// <summary>
/// Aggregates per-player projections into one team offensive-production number.
///
/// UNITS WARNING — read before using this for anything market-facing:
/// the output is a sum of weekly <em>fantasy</em> points in the connected league's scoring
/// format. It is NOT NFL points scored. Two independent proofs:
///   1. The value changes when the user's fantasy league switches PPR → Standard. Real NFL
///      scoring does not depend on anyone's fantasy settings.
///   2. PPR awards ~1 point per reception; an NFL offense records ~20+ receptions/carries per
///      game, contributing points with no correspondence to the scoreboard at all.
/// Converting this index into expected NFL points requires a calibration Playbook does not yet
/// have (see TeamGameProjectionService). Until then it must not be compared to a points line.
/// </summary>
public static class TeamProductionIndexCalculator
{
    /// <summary>Maximum upward health adjustment (a very healthy player is only marginally above baseline).</summary>
    public const decimal MaxHealthBoost = 0.05m;

    /// <summary>Maximum downward health adjustment (health problems genuinely suppress production).</summary>
    public const decimal MaxHealthPenalty = 0.30m;

    /// <summary>Conservative assumed momentum adjustment for a confirmed usage trend. Not a learned value.</summary>
    public const decimal TrendAdjustment = 0.05m;

    /// <summary>
    /// Bounded health adjustment anchored to Playbook's HealthScore semantics
    /// (0–100, 50 = neutral, higher = healthier — see PlayerIntelligenceProfile.HealthScore).
    ///
    /// Deliberately asymmetric: the underlying projection already assumes a broadly available
    /// player, so extra health adds little (max +5%), while a compromised player is meaningfully
    /// reduced (max −30%). A missing score returns exactly 1.0 — unknown health is never
    /// treated as bad health.
    /// </summary>
    public static decimal HealthMultiplier(int? healthScore)
    {
        if (healthScore is not int score)
        {
            return 1m;
        }

        var clamped = Math.Clamp(score, 0, 100);
        var deviation = (clamped - 50m) / 50m; // −1.0 … +1.0

        return deviation >= 0
            ? 1m + deviation * MaxHealthBoost
            : 1m + deviation * MaxHealthPenalty;
    }

    /// <summary>Conservative trend adjustment. Unknown/Stable/Volatile trends are neutral.</summary>
    public static decimal TrendMultiplier(StatisticalTrendSignal trend) => trend switch
    {
        StatisticalTrendSignal.Increasing => 1m + TrendAdjustment,
        StatisticalTrendSignal.Decreasing => 1m - TrendAdjustment,
        _ => 1m
    };

    /// <summary>
    /// Aggregate a team's offensive production.
    ///
    /// QB handling is deliberately non-multiplicative. The quarterback's contribution is
    /// represented by <em>whose projection is included</em> — the highest-projected QB who is not
    /// ruled out. If the QB1 is ruled out, the backup's own (lower) real projection is used.
    /// No invented replacement multiplier is applied to the rest of the offense, because that
    /// would double-count the QB against his own already-included projection.
    ///
    /// Returns null when the data is insufficient (no QB, or no skill players) rather than
    /// guessing.
    /// </summary>
    public static TeamProductionIndex? Compute(IReadOnlyList<TeamPlayerProductionInput> players)
    {
        ArgumentNullException.ThrowIfNull(players);

        var quarterbacks = players.Where(p => p.Position == Position.QB).ToList();
        var skill = players
            .Where(p => p.Position is Position.RB or Position.WR or Position.TE)
            .ToList();

        var availableSkill = skill.Where(p => !p.IsRuledOut).ToList();
        if (availableSkill.Count == 0)
        {
            return null;
        }

        // Highest-projected QB overall identifies the nominal starter; the highest-projected QB
        // who is not ruled out is who actually plays.
        var nominalStarter = quarterbacks
            .OrderByDescending(p => p.ProjectedFantasyPoints)
            .FirstOrDefault();
        var playingQb = quarterbacks
            .Where(p => !p.IsRuledOut)
            .OrderByDescending(p => p.ProjectedFantasyPoints)
            .FirstOrDefault();

        if (nominalStarter is null || playingQb is null)
        {
            // No QB data at all, or every QB ruled out and no backup projection exists.
            return null;
        }

        var starterRuledOut = nominalStarter.IsRuledOut;

        var qbProduction = Adjust(playingQb);
        var skillProduction = availableSkill.Sum(Adjust);

        var ruledOut = skill.Count(p => p.IsRuledOut) + quarterbacks.Count(p => p.IsRuledOut);

        var explanation =
            $"{availableSkill.Count} skill players + QB; " +
            $"QB production {qbProduction:0.0} (starter{(starterRuledOut ? " ruled out — backup projection used" : " available")}); " +
            $"{ruledOut} ruled-out player(s) excluded; health/trend adjusted; " +
            $"aggregate {qbProduction + skillProduction:0.0} fantasy pts";

        return new TeamProductionIndex
        {
            FantasyProductionPoints = Math.Round(qbProduction + skillProduction, 1, MidpointRounding.AwayFromZero),
            SkillPlayersCounted = availableSkill.Count,
            RuledOutCount = ruledOut,
            StartingQuarterbackRuledOut = starterRuledOut,
            QuarterbackProduction = Math.Round(qbProduction, 1, MidpointRounding.AwayFromZero),
            Explanation = explanation
        };
    }

    private static decimal Adjust(TeamPlayerProductionInput player) =>
        player.ProjectedFantasyPoints
        * HealthMultiplier(player.HealthScore)
        * TrendMultiplier(player.Trend);
}
