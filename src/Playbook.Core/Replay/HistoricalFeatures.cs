using Playbook.Core.Players;

namespace Playbook.Core.Replay;

/// <summary>
/// How much prior history supports a reconstructed feature or projection.
/// </summary>
public enum DataSufficiency
{
    /// <summary>Enough prior games for a stable rolling window (typically 3+).</summary>
    Sufficient = 0,

    /// <summary>Some history exists (typically 1–2 games) but the window is thin.</summary>
    Limited = 1,

    /// <summary>No usable prior games at the cutoff.</summary>
    Insufficient = 2
}

/// <summary>
/// One completed game observation known before the information cutoff.
/// Never includes the target week's actuals.
/// </summary>
public sealed class HistoricalGameObservation
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required double FantasyPoints { get; init; }

    public int? PassAttempts { get; init; }

    public int? PassYards { get; init; }

    public int? PassTouchdowns { get; init; }

    public int? RushAttempts { get; init; }

    public int? RushYards { get; init; }

    public int? RushTouchdowns { get; init; }

    public int? Targets { get; init; }

    public int? Receptions { get; init; }

    public int? ReceivingYards { get; init; }

    public int? ReceivingTouchdowns { get; init; }

    public double? OffenseSnapPct { get; init; }
}

/// <summary>
/// Cutoff-safe reconstructed features for one player. All rolling windows use prior weeks only.
/// </summary>
public sealed class HistoricalPlayerFeatures
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Position Position { get; init; }

    public required string Team { get; init; }

    public required int Season { get; init; }

    public required int TargetWeek { get; init; }

    public required DateTimeOffset InformationCutoff { get; init; }

    /// <summary>Prior REG weeks that contributed (ascending), never including <see cref="TargetWeek"/>.</summary>
    public required IReadOnlyList<int> SourceWeeks { get; init; }

    public required int GamesUsed { get; init; }

    public required DataSufficiency Sufficiency { get; init; }

    public double? FantasyPointsPerGame { get; init; }

    public double? Last1FantasyPoints { get; init; }

    public double? Last3FantasyPointsAvg { get; init; }

    public double? Last5FantasyPointsAvg { get; init; }

    public double? AvgTargets { get; init; }

    public double? AvgReceptions { get; init; }

    public double? AvgRushAttempts { get; init; }

    public double? AvgPassAttempts { get; init; }

    public double? AvgTouches { get; init; }

    public double? AvgReceivingYards { get; init; }

    public double? AvgRushYards { get; init; }

    public double? AvgPassYards { get; init; }

    public double? AvgTouchdowns { get; init; }

    public double? AvgOffenseSnapPct { get; init; }

    public int? OpportunityScore { get; init; }

    public int? UsageScore { get; init; }

    public int? RecentProductionScore { get; init; }

    public string? RoleNote { get; init; }

    public required IReadOnlyList<string> UnavailableFeatures { get; init; }

    public required int FeatureConfidence { get; init; }
}

/// <summary>
/// Transparent pre-week expectation produced from reconstructed features only.
/// </summary>
public sealed class HistoricalProjection
{
    public required string ModelId { get; init; }

    public required string ModelLabel { get; init; }

    public required double ProjectedPoints { get; init; }

    public double? Floor { get; init; }

    public double? Ceiling { get; init; }

    public required int ProjectionConfidence { get; init; }

    public required DataSufficiency Sufficiency { get; init; }

    public required IReadOnlyList<int> SourceWeeks { get; init; }

    public required string Methodology { get; init; }

    public bool IsValid => Sufficiency != DataSufficiency.Insufficient;
}

/// <summary>
/// Side-by-side baseline projections for scientific comparison (not decision ranking alone).
/// </summary>
public sealed class HistoricalProjectionBundle
{
    public required HistoricalPlayerFeatures Features { get; init; }

    /// <summary>Primary projection fed into the Knowledge/Decision engines.</summary>
    public required HistoricalProjection Primary { get; init; }

    public required HistoricalProjection BaselineRecentAverage { get; init; }

    /// <summary>Projection V1 (uncalibrated opportunity-aware).</summary>
    public required HistoricalProjection BaselineOpportunityAware { get; init; }

    /// <summary>Projection V2 (calibrated). Null only if calibration engine unavailable.</summary>
    public HistoricalProjection? ProjectionV2 { get; init; }
}
