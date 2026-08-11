using Playbook.Core.Players;

namespace Playbook.Application.Projections;

/// <summary>
/// Extensions for ProjectionRuleOptions to support era-segmented baselines.
/// </summary>
public static class ProjectionRuleOptionsExtensions
{
    /// <summary>
    /// Gets era-segmented baselines (Era A: 2012–2019, Era B: 2020–2023).
    /// Pre-committed domain-grounded estimates reflecting NFL passing-game evolution.
    /// </summary>
    public static (Dictionary<string, decimal> EraA, Dictionary<string, decimal> EraB) GetEraSegmentedBaselines()
    {
        var eraA = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.QB)] = 18.0m,
            [nameof(Position.RB)] = 12.0m,
            [nameof(Position.WR)] = 10.8m,
            [nameof(Position.TE)] = 7.5m,
            [nameof(Position.K)] = 8.0m,
            [nameof(Position.DST)] = 8.0m
        };

        var eraB = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.QB)] = 18.5m,
            [nameof(Position.RB)] = 11.5m,
            [nameof(Position.WR)] = 11.5m,
            [nameof(Position.TE)] = 8.5m,
            [nameof(Position.K)] = 8.0m,
            [nameof(Position.DST)] = 8.0m
        };

        return (eraA, eraB);
    }

    /// <summary>
    /// Creates a frozen baseline provider (control condition).
    /// </summary>
    public static IProjectionBaselineProvider CreateFrozenBaselineProvider(this ProjectionRuleOptions rules)
    {
        return new FrozenBaselineProvider(rules.PositionBaselines);
    }

    /// <summary>
    /// Creates an era-segmented baseline provider (experimental condition).
    /// </summary>
    public static IProjectionBaselineProvider CreateEraSegmentedBaselineProvider()
    {
        var (eraA, eraB) = GetEraSegmentedBaselines();
        return new EraSegmentedBaselineProvider(eraA, eraB);
    }
}
