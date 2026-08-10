using Playbook.Core.Leagues;

namespace Playbook.Core.Projections.Models;

/// <summary>
/// Numerical expected-outcome estimate for one player in one league/week context.
/// Does not encode start/sit, waiver, draft, or trade decisions.
/// </summary>
public sealed class PlayerProjection
{
    public required Guid PlayerId { get; init; }

    public Guid? LeagueId { get; init; }

    public required int Week { get; init; }

    public required ScoringType ScoringFormat { get; init; }

    /// <summary>Primary expected fantasy points (same as <see cref="Median"/>).</summary>
    public required decimal ProjectedFantasyPoints { get; init; }

    /// <summary>Alias of projected points for API clarity.</summary>
    public decimal ProjectedPoints => ProjectedFantasyPoints;

    public required decimal Floor { get; init; }

    public required decimal Median { get; init; }

    public required decimal Ceiling { get; init; }

    /// <summary>0–100 confidence in the projection (not P(exact score)).</summary>
    public required int Confidence { get; init; }

    /// <summary>0–100 volatility (higher = wider outcome range).</summary>
    public required int Volatility { get; init; }

    public required IReadOnlyList<string> ProjectionReasoning { get; init; }

    public required IReadOnlyList<string> SupportingIntelligence { get; init; }

    public required DateTimeOffset ProjectionTimestamp { get; init; }

    public required string ProjectionVersion { get; init; }

    public required ProjectionInputsUsed InputsUsed { get; init; }

    /// <summary>Legacy alias for timestamp.</summary>
    public DateTimeOffset LastUpdated => ProjectionTimestamp;
}
