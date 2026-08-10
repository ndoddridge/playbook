using Playbook.Core.Decisions;

namespace Playbook.Core.Knowledge;

/// <summary>
/// One piece of structured evidence in the shared knowledge layer.
/// Knowledge is not a prediction — it describes what is known at a cutoff.
/// </summary>
public sealed class KnowledgeEvidence
{
    public required KnowledgeScope Scope { get; init; }

    public required KnowledgeAspect Aspect { get; init; }

    public required string Statement { get; init; }

    public required SignalDirection Direction { get; init; }

    public required SignalStrength Strength { get; init; }

    public required EvidenceStatus Status { get; init; }

    /// <summary>0–100 confidence in this evidence item itself.</summary>
    public required int Confidence { get; init; }

    public required EvidenceReliability Reliability { get; init; }

    public required string Source { get; init; }

    /// <summary>When the underlying observation was known.</summary>
    public DateTimeOffset? ObservedAt { get; init; }

    /// <summary>
    /// Information cutoff that bounded this item.
    /// Evidence with ObservedAt after this cutoff must never appear.
    /// </summary>
    public DateTimeOffset? InformationCutoff { get; init; }

    public double? Value { get; init; }

    /// <summary>True when this row exists only to record that an aspect is unavailable.</summary>
    public bool IsUnavailableMarker { get; init; }

    public string? Category { get; init; }
}
