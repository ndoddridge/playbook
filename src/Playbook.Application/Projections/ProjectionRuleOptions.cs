using Playbook.Core.Players;

namespace Playbook.Application.Projections;

/// <summary>
/// Centralized, configurable projection adjustment rules for Engine v0.1.
/// Bound from <c>Projection:Rules</c>.
/// </summary>
public sealed class ProjectionRuleOptions
{
    public const string SectionName = "Projection:Rules";

    public decimal OpportunityVolumeFactor { get; set; } = 0.18m;

    public decimal UsageVolumeFactor { get; set; } = 0.16m;

    public decimal RecencyFactor { get; set; } = 0.12m;

    public decimal HealthDownsideFactor { get; set; } = 0.30m;

    public decimal HealthUpsideFactor { get; set; } = 0.06m;

    public decimal RiskDownsideFactor { get; set; } = 0.18m;

    public decimal TrendFactor { get; set; } = 0.10m;

    public decimal MomentumFactor { get; set; } = 0.05m;

    /// <summary>Max absolute point swing from matchup when available.</summary>
    public decimal MatchupMaxSwing { get; set; } = 1.8m;

    /// <summary>Max absolute point swing from game environment when available.</summary>
    public decimal EnvironmentMaxSwing { get; set; } = 1.2m;

    /// <summary>Floor band width as fraction of median at volatility=0.</summary>
    public decimal FloorSigmaMin { get; set; } = 0.10m;

    /// <summary>Floor band width as fraction of median at volatility=100.</summary>
    public decimal FloorSigmaMax { get; set; } = 0.42m;

    /// <summary>Ceiling band width as fraction of median at volatility=0.</summary>
    public decimal CeilingSigmaMin { get; set; } = 0.12m;

    /// <summary>Ceiling band width as fraction of median at volatility=100.</summary>
    public decimal CeilingSigmaMax { get; set; } = 0.48m;

    public double VolatilityFromLowConfidence { get; set; } = 0.35;

    public int BaselineVolatility { get; set; } = 16;

    public double IntelligenceConfidenceWeight { get; set; } = 0.55;

    public decimal MinProjection { get; set; } = 0m;

    public decimal MaxProjection { get; set; } = 45m;

    /// <summary>Weight of recent game-log weekly pts when sample ≥ this many games.</summary>
    public int StrongRecentSampleGames { get; set; } = 4;

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
