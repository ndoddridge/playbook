using Playbook.Core.Players;

namespace Playbook.Core.Intelligence.Models;

/// <summary>
/// Aggregated football intelligence for one player.
/// Downstream engines should request this instead of collecting facts manually.
/// </summary>
public sealed class PlayerIntelligence
{
    public required Guid PlayerId { get; init; }
    public required int OverallConfidence { get; init; }
    public required IReadOnlyList<IntelligenceFact> Facts { get; init; }
    public required string TrendSummary { get; init; }
    public required string RiskSummary { get; init; }
    public required string OpportunitySummary { get; init; }
    public required DateTimeOffset LastUpdated { get; init; }

    /// <summary>
    /// Reuses the shared player trend vocabulary (<see cref="TrendDirection"/>).
    /// </summary>
    public TrendDirection TrendDirection { get; init; } = TrendDirection.Flat;
}
