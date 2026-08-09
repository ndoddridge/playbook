namespace Playbook.Core.Decisions;

/// <summary>
/// Centralized knowledge snapshot for one player at a point in context.
/// Separates facts (observations) from signals (structured evidence).
/// Does not invent missing data — unknowns are explicit.
/// </summary>
public sealed class PlayerKnowledge
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required string PositionLabel { get; init; }

    public required IReadOnlyList<KnowledgeFact> Facts { get; init; }

    public required IReadOnlyList<KnowledgeSignal> Signals { get; init; }

    public required EvidenceStatus OverallStatus { get; init; }

    /// <summary>0–100 evidence-quality score for this knowledge snapshot.</summary>
    public required int KnowledgeConfidence { get; init; }

    public required IReadOnlyList<string> MissingEvidence { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public DateTimeOffset? InformationCutoff { get; init; }

    public decimal? ProjectedPoints { get; init; }

    public decimal? Floor { get; init; }

    public decimal? Ceiling { get; init; }

    public int? ProjectionConfidence { get; init; }

    public int? OpportunityScore { get; init; }

    public int? UsageScore { get; init; }

    public string? HealthLabel { get; init; }
}

/// <summary>
/// An observed fact — not an inference or decision.
/// Example: "Player is currently listed as Questionable (ankle)."
/// </summary>
public sealed class KnowledgeFact
{
    public required string Key { get; init; }

    public required string Statement { get; init; }

    public required string Source { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }

    public required EvidenceStatus Status { get; init; }
}
