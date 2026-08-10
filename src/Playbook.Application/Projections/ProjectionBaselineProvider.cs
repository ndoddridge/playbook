using Playbook.Core.Players;

namespace Playbook.Application.Projections;

/// <summary>
/// Provides position baselines with optional era segmentation.
/// Era A: 2012–2019; Era B: 2020–2023.
/// Domain-grounded estimates reflecting NFL passing-game evolution.
/// </summary>
public interface IProjectionBaselineProvider
{
    /// <summary>
    /// Get baseline weekly projection for a position in a given season.
    /// </summary>
    decimal GetBaseline(Position position, int season);

    /// <summary>
    /// Gets the era (A or B) for a given season.
    /// </summary>
    string GetEra(int season);
}

/// <summary>
/// Control baseline provider — returns frozen, era-agnostic baselines.
/// Used as the control condition in experiments.
/// </summary>
public sealed class FrozenBaselineProvider : IProjectionBaselineProvider
{
    private readonly Dictionary<string, decimal> _baselines;

    public FrozenBaselineProvider(Dictionary<string, decimal> baselines)
    {
        _baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
    }

    public decimal GetBaseline(Position position, int season)
    {
        var key = position.ToString();
        return _baselines.TryGetValue(key, out var baseline) ? baseline : 10m;
    }

    public string GetEra(int season) => "control";
}

/// <summary>
/// Experimental era-segmented baseline provider.
/// Era A (2012–2019): Pre-passing-game-evolution baselines.
/// Era B (2020–2023): Post-passing-game-evolution baselines.
/// 
/// Rationale:
/// - QB: Stable across eras (18.0 → 18.5, reflecting slight inflation)
/// - RB: Declining (12.0 → 11.5, fewer dual-threat opportunities)
/// - WR: Rising (10.8 → 11.5, increased target share)
/// - TE: Rising (7.5 → 8.5, elevated in fantasy-relevant formats)
/// - K/DST: Stable (8.0 → 8.0, position-agnostic to passing game)
/// </summary>
public sealed class EraSegmentedBaselineProvider : IProjectionBaselineProvider
{
    private readonly Dictionary<string, decimal> _baselinesEraA;
    private readonly Dictionary<string, decimal> _baselinesEraB;

    public EraSegmentedBaselineProvider(
        Dictionary<string, decimal> baselinesEraA,
        Dictionary<string, decimal> baselinesEraB)
    {
        _baselinesEraA = baselinesEraA ?? throw new ArgumentNullException(nameof(baselinesEraA));
        _baselinesEraB = baselinesEraB ?? throw new ArgumentNullException(nameof(baselinesEraB));
    }

    public decimal GetBaseline(Position position, int season)
    {
        var key = position.ToString();
        var baselines = GetEra(season) == "A" ? _baselinesEraA : _baselinesEraB;
        return baselines.TryGetValue(key, out var baseline) ? baseline : 10m;
    }

    public string GetEra(int season) => season <= 2019 ? "A" : "B";
}
