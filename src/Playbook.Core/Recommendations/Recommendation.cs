namespace Playbook.Core.Recommendations;

/// <summary>
/// Central recommendation payload produced by engines and consumed by the UI.
/// Extensible: add optional metadata without breaking existing renderers.
/// </summary>
public sealed class Recommendation
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required RecommendationType ActionType { get; init; }

    public required RecommendationPriority Priority { get; init; }

    /// <summary>Confidence score from 0 to 100.</summary>
    public required int Confidence { get; init; }

    public required string Impact { get; init; }

    public required RecommendationCategory Category { get; init; }

    public required RecommendationStatus Status { get; init; }

    public required string Reasoning { get; init; }

    public required IReadOnlyList<string> SupportingSignals { get; init; }

    public required IReadOnlyList<string> Evidence { get; init; }

    public string? FutureNotes { get; init; }

    public required DateTimeOffset LastUpdated { get; init; }

    public required EngineType SourceEngine { get; init; }

    /// <summary>
    /// Optional UI hint for initial expand state. Components may still manage expand locally.
    /// </summary>
    public bool IsExpanded { get; init; }

    /// <summary>
    /// Optional bag for engine-specific metadata without changing the core contract.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
