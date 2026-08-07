namespace Playbook.Core.Decisions;

/// <summary>
/// Generic recommendation payload for UI rendering.
/// Domain-agnostic: engines populate this; the Decision Card only displays it.
/// </summary>
public sealed class Decision
{
    public required DecisionActionType ActionType { get; init; }

    public required string Title { get; init; }

    /// <summary>Confidence score from 0 to 100.</summary>
    public required int Confidence { get; init; }

    public required DecisionPriority Priority { get; init; }

    public required string Impact { get; init; }

    public required DecisionStatus Status { get; init; }

    public required string Summary { get; init; }

    public required string Category { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string Reasoning { get; init; }

    public required IReadOnlyList<string> SupportingSignals { get; init; }

    public required IReadOnlyList<string> Evidence { get; init; }

    public string? FutureNotes { get; init; }
}
