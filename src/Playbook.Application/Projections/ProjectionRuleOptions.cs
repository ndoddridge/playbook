using Playbook.Core.Players;

namespace Playbook.Application.Projections;

/// <summary>
/// Centralized, configurable projection adjustment rules.
/// Bound from <c>Projection:Rules</c>.
/// </summary>
public sealed class ProjectionRuleOptions
{
    public const string SectionName = "Projection:Rules";

    /// <summary>
    /// Max relative volume swing from Opportunity (score 100 → +factor, score 0 → −factor).
    /// </summary>
    public decimal OpportunityVolumeFactor { get; set; } = 0.22m;

    /// <summary>Max relative swing from Usage on the median.</summary>
    public decimal UsageVolumeFactor { get; set; } = 0.14m;

    /// <summary>Max relative reduction from poor Health (score 0).</summary>
    public decimal HealthDownsideFactor { get; set; } = 0.30m;

    /// <summary>Small relative lift when Health is strong (score 100).</summary>
    public decimal HealthUpsideFactor { get; set; } = 0.06m;

    /// <summary>Max relative reduction from elevated Risk (0–100, 0-based).</summary>
    public decimal RiskDownsideFactor { get; set; } = 0.18m;

    /// <summary>Usage-driven ceiling expansion factor (above neutral usage).</summary>
    public decimal UsageCeilingFactor { get; set; } = 0.20m;

    /// <summary>Trend Up / Down median nudge factor.</summary>
    public decimal TrendFactor { get; set; } = 0.08m;

    /// <summary>Base floor distance below median (points) before volatility/health.</summary>
    public decimal BaseFloorSpread { get; set; } = 2.8m;

    /// <summary>Base ceiling distance above median (points) before usage/volatility.</summary>
    public decimal BaseCeilingSpread { get; set; } = 3.2m;

    /// <summary>Volatility points added per missing confidence point (100 − confidence).</summary>
    public double VolatilityFromLowConfidence { get; set; } = 0.40;

    /// <summary>Baseline volatility before confidence/health adjustments.</summary>
    public int BaselineVolatility { get; set; } = 18;

    /// <summary>
    /// Confidence blend: weight of intelligence confidence vs production-source confidence.
    /// </summary>
    public double IntelligenceConfidenceWeight { get; set; } = 0.65;

    public decimal MinProjection { get; set; } = 0m;

    public decimal MaxProjection { get; set; } = 45m;

    /// <summary>Legacy position shells retained for docs/config compatibility (not primary baselines).</summary>
    public Dictionary<string, decimal> PositionBaselines { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Position.QB)] = 18.0m,
        [nameof(Position.RB)] = 12.0m,
        [nameof(Position.WR)] = 11.0m,
        [nameof(Position.TE)] = 8.0m,
        [nameof(Position.K)] = 8.0m,
        [nameof(Position.DST)] = 8.0m
    };
}
