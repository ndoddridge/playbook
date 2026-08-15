namespace Playbook.Core.Draft;

public enum FactorDirection
{
    Positive,
    Negative,
    Neutral
}

/// <summary>One explainable input into a <see cref="DraftRecommendation"/> — mirrors the existing
/// PredictionSignalContribution / IntelligenceFactor pattern used elsewhere in Playbook so
/// reasoning stays structured and inspectable, never an opaque score.</summary>
public sealed class DraftRecommendationFactor
{
    public required string Label { get; init; }

    public string? Detail { get; init; }

    public required FactorDirection Direction { get; init; }

    /// <summary>False when this factor's underlying signal isn't available for this player — the
    /// factor is still listed (transparently) rather than silently omitted.</summary>
    public required bool Available { get; init; }
}
