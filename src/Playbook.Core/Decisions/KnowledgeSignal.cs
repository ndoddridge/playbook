namespace Playbook.Core.Decisions;

/// <summary>
/// Structured evidence unit consumed by the decision engine.
/// Prefer these over free-form UI strings when synthesizing decisions.
/// </summary>
public sealed class KnowledgeSignal
{
    public required SignalType Type { get; init; }

    /// <summary>Optional numeric value (projection points, opportunity score, etc.).</summary>
    public double? Value { get; init; }

    public required SignalDirection Direction { get; init; }

    public required SignalStrength Strength { get; init; }

    /// <summary>0–100 confidence in this signal itself (not the final decision).</summary>
    public required int Confidence { get; init; }

    public required EvidenceStatus Status { get; init; }

    public required string Source { get; init; }

    public required string Explanation { get; init; }

    /// <summary>When the underlying observation was known / last updated, if available.</summary>
    public DateTimeOffset? ObservedAt { get; init; }

    public string? Category { get; init; }
}

public enum SignalType
{
    Projection = 0,
    Floor = 1,
    Ceiling = 2,
    RecentProduction = 3,
    Opportunity = 4,
    Usage = 5,
    Health = 6,
    Role = 7,
    News = 8,
    MatchupContext = 9,
    Volatility = 10,
    Coverage = 11,
    Outlook = 12,
    AlternativeComparison = 13
}

public enum SignalDirection
{
    Positive = 0,
    Negative = 1,
    Neutral = 2,
    Uncertainty = 3
}

public enum SignalStrength
{
    Weak = 0,
    Moderate = 1,
    Strong = 2
}
