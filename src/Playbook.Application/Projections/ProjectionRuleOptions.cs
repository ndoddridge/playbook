using Playbook.Core.Players;

namespace Playbook.Application.Projections;

/// <summary>
/// Centralized, configurable weighted projection rules.
/// Bound from <c>Projection:Rules</c>.
/// </summary>
public sealed class ProjectionRuleOptions
{
    public const string SectionName = "Projection:Rules";

    /// <summary>Baseline weekly fantasy points by position (standard scoring).</summary>
    public Dictionary<string, decimal> PositionBaselines { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Position.QB)] = 18.0m,
        [nameof(Position.RB)] = 12.0m,
        [nameof(Position.WR)] = 11.0m,
        [nameof(Position.TE)] = 8.0m,
        [nameof(Position.K)] = 8.0m,
        [nameof(Position.DST)] = 8.0m
    };

    /// <summary>Points added for Half-PPR / PPR by position.</summary>
    public Dictionary<string, decimal> HalfPprBoosts { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Position.RB)] = 0.5m,
        [nameof(Position.WR)] = 1.0m,
        [nameof(Position.TE)] = 0.8m
    };

    public Dictionary<string, decimal> PprBoosts { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Position.RB)] = 1.0m,
        [nameof(Position.WR)] = 2.0m,
        [nameof(Position.TE)] = 1.5m
    };

    /// <summary>Max fantasy-point swing when health is at 0 or 100 (vs baseline 50).</summary>
    public decimal HealthWeight { get; set; } = 4.0m;

    /// <summary>Max fantasy-point swing from opportunity score.</summary>
    public decimal OpportunityWeight { get; set; } = 5.0m;

    /// <summary>Median contribution from usage; ceiling uses a larger share.</summary>
    public decimal UsageWeight { get; set; } = 3.0m;

    /// <summary>Max point reduction when risk is elevated.</summary>
    public decimal RiskWeight { get; set; } = 3.5m;

    /// <summary>Small median nudge from news momentum.</summary>
    public decimal MomentumWeight { get; set; } = 1.0m;

    /// <summary>Extra ceiling points when usage is above neutral.</summary>
    public decimal UsageCeilingBonus { get; set; } = 4.0m;

    /// <summary>Base floor distance below median (before volatility).</summary>
    public decimal BaseFloorSpread { get; set; } = 3.5m;

    /// <summary>Base ceiling distance above median (before usage/volatility).</summary>
    public decimal BaseCeilingSpread { get; set; } = 4.0m;

    /// <summary>Volatility points added per missing confidence point (100 − confidence).</summary>
    public double VolatilityFromLowConfidence { get; set; } = 0.45;

    /// <summary>Baseline volatility before confidence/health adjustments.</summary>
    public int BaselineVolatility { get; set; } = 20;

    public decimal MinProjection { get; set; } = 0m;

    public decimal MaxProjection { get; set; } = 45m;
}
