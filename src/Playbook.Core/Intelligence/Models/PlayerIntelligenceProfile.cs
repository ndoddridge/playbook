using Playbook.Core.Players;

namespace Playbook.Core.Intelligence.Models;

/// <summary>
/// Canonical aggregated intelligence for one player.
/// Projection / Prediction / Decision engines should consume this — not raw facts.
/// </summary>
public sealed class PlayerIntelligenceProfile
{
    public required Guid PlayerId { get; init; }

    public required int OverallConfidence { get; init; }

    /// <summary>0–100 risk score (higher = more risk).</summary>
    public required int OverallRisk { get; init; }

    /// <summary>0–100 opportunity score (50 = neutral).</summary>
    public required int OpportunityScore { get; init; }

    public required TrendDirection TrendDirection { get; init; }

    /// <summary>0–100 health score (50 = neutral, higher = healthier).</summary>
    public required int HealthScore { get; init; }

    /// <summary>0–100 usage score (50 = neutral).</summary>
    public required int UsageScore { get; init; }

    /// <summary>0–100 news momentum (higher = more recent/important coverage).</summary>
    public required int NewsMomentum { get; init; }

    public required DateTimeOffset LastUpdated { get; init; }

    public required IReadOnlyList<IntelligenceFact> SupportingFacts { get; init; }

    /// <summary>Short dashboard headline, e.g. "Opportunity Increasing".</summary>
    public required string Headline { get; init; }

    /// <summary>Primary change signal for Top Intelligence Changes.</summary>
    public required IntelligenceChangeSignal ChangeSignal { get; init; }
}

public enum IntelligenceChangeSignal
{
    Neutral = 0,
    OpportunityIncreasing = 1,
    UsageIncreasing = 2,
    HealthImproving = 3,
    HealthConcern = 4,
    OpportunityDecreasing = 5,
    ElevatedRisk = 6
}
