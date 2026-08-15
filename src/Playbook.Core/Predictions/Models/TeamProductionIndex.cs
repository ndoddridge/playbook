using Playbook.Core.Players;
using Playbook.Core.Stats.Models;

namespace Playbook.Core.Predictions.Models;

/// <summary>
/// One player's real inputs to the team offensive-production aggregate.
/// Every field is sourced from existing Playbook services — nothing is inferred here.
/// </summary>
public sealed record TeamPlayerProductionInput
{
    public required Position Position { get; init; }

    /// <summary>Weekly projected fantasy points in the connected league's scoring format.</summary>
    public required decimal ProjectedFantasyPoints { get; init; }

    /// <summary>True when a verified injury designation rules the player out (Out/IR/PUP/Suspension).</summary>
    public bool IsRuledOut { get; init; }

    /// <summary>0–100 health score (50 = neutral). Null when no intelligence profile exists.</summary>
    public int? HealthScore { get; init; }

    public StatisticalTrendSignal Trend { get; init; } = StatisticalTrendSignal.Unknown;
}

/// <summary>
/// Aggregate offensive production for one team.
///
/// IMPORTANT UNITS: this is a sum of weekly <em>fantasy</em> points in the connected league's
/// scoring format — it is NOT a projection of NFL points scored. The two are not
/// interchangeable, and the distinction is deliberately encoded in the type name so no caller
/// can accidentally compare this against a sportsbook points line. See
/// <see cref="TeamProductionIndexCalculator"/> for why.
/// </summary>
public sealed record TeamProductionIndex
{
    /// <summary>Aggregate fantasy-point production (QB + skill), health/trend adjusted.</summary>
    public required decimal FantasyProductionPoints { get; init; }

    /// <summary>Number of non-ruled-out skill players (RB/WR/TE) contributing.</summary>
    public required int SkillPlayersCounted { get; init; }

    /// <summary>Number of players excluded because a verified designation rules them out.</summary>
    public required int RuledOutCount { get; init; }

    /// <summary>True when the highest-projected QB is ruled out and a backup's projection was used.</summary>
    public required bool StartingQuarterbackRuledOut { get; init; }

    /// <summary>The QB projection actually included (already health/trend adjusted).</summary>
    public required decimal QuarterbackProduction { get; init; }

    public required string Explanation { get; init; }
}
