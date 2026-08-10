namespace Playbook.Core.Stats.Models;

/// <summary>
/// Clean statistical interface for the Intelligence Engine.
/// Values are derived from normalized Playbook statistics — never provider-specific DTOs.
/// </summary>
public sealed class PlayerStatisticalContext
{
    public required Guid PlayerId { get; init; }

    public required DateTimeOffset AsOf { get; init; }

    /// <summary>Recent NFL production window (e.g. last N games or current season pace).</summary>
    public StatisticalProductionWindow? RecentProduction { get; init; }

    /// <summary>Multi-season NFL historical window (college excluded).</summary>
    public StatisticalProductionWindow? HistoricalProduction { get; init; }

    /// <summary>NFL career baseline (college excluded).</summary>
    public StatisticalProductionWindow? CareerBaseline { get; init; }

    /// <summary>College production when relevant (especially &lt; 3 NFL seasons). Never merged into NFL samples.</summary>
    public StatisticalProductionWindow? CollegeProduction { get; init; }

    public StatisticalUsageSignals? Usage { get; init; }

    public StatisticalEfficiencySignals? Efficiency { get; init; }

    public StatisticalConsistencySignals? Consistency { get; init; }

    public decimal? VolatilityScore { get; init; }

    public StatisticalTrendSignal Trend { get; init; } = StatisticalTrendSignal.Unknown;

    public int GameLogsAvailable { get; init; }

    public int NflSeasonsAvailable { get; init; }

    public bool HasCollegeStatistics { get; init; }

    public string? PrimarySourceProvider { get; init; }
}

public sealed class StatisticalProductionWindow
{
    public required FootballLevel Level { get; init; }

    public required string Label { get; init; }

    public int? Games { get; init; }

    public CanonicalCountingStats? Totals { get; init; }

    public CanonicalCountingStats? PerGame { get; init; }
}

public sealed class StatisticalUsageSignals
{
    public decimal? CarriesPerGame { get; init; }

    public decimal? TargetsPerGame { get; init; }

    public decimal? PassAttemptsPerGame { get; init; }

    public decimal? ReceptionsPerGame { get; init; }

    public string? WorkloadTrend { get; init; }
}

public sealed class StatisticalEfficiencySignals
{
    public decimal? YardsPerCarry { get; init; }

    public decimal? YardsPerTarget { get; init; }

    public decimal? YardsPerReception { get; init; }

    public decimal? CompletionPercentage { get; init; }

    public decimal? YardsPerPassAttempt { get; init; }
}

public sealed class StatisticalConsistencySignals
{
    public decimal? CoefficientOfVariation { get; init; }

    public int? GamesWithCountingStats { get; init; }

    public decimal? MedianWeeklyFantasyPpr { get; init; }
}

public enum StatisticalTrendSignal
{
    Unknown = 0,
    Increasing = 1,
    Stable = 2,
    Decreasing = 3,
    Volatile = 4
}
