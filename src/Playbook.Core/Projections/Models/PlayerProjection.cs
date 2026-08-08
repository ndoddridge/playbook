namespace Playbook.Core.Projections.Models;

/// <summary>
/// Numerical expected-outcome estimate for one player.
/// Does not encode start/sit, waiver, draft, or trade decisions.
/// Future Decision / Quick Picks / Draft / Waiver / Trade engines consume this.
/// </summary>
public sealed class PlayerProjection
{
    public required Guid PlayerId { get; init; }

    /// <summary>Primary expected fantasy points for the projection window.</summary>
    public required decimal ProjectedFantasyPoints { get; init; }

    public required decimal Floor { get; init; }

    public required decimal Median { get; init; }

    public required decimal Ceiling { get; init; }

    /// <summary>0–100 confidence in the projection.</summary>
    public required int Confidence { get; init; }

    /// <summary>0–100 volatility (higher = wider outcome range).</summary>
    public required int Volatility { get; init; }

    /// <summary>Explainable rule-by-rule reasoning for the estimate.</summary>
    public required IReadOnlyList<string> ProjectionReasoning { get; init; }

    /// <summary>Intelligence scores and facts that supported the projection.</summary>
    public required IReadOnlyList<string> SupportingIntelligence { get; init; }

    public required DateTimeOffset LastUpdated { get; init; }
}
