namespace Playbook.Core.Injuries.Models;

/// <summary>
/// Possible injury concern from news/practice buzz. Never treated as a confirmed injury record.
/// </summary>
public sealed class UnconfirmedInjurySignal
{
    public required Guid Id { get; init; }

    public required Guid PlayerId { get; init; }

    public required string Headline { get; init; }

    public string? Detail { get; init; }

    public string? BodyPart { get; init; }

    public required string Source { get; init; }

    public string? SourceUrl { get; init; }

    public required DateTimeOffset Published { get; init; }

    public DateTimeOffset LastUpdated { get; init; }

    /// <summary>0–100 confidence that the report reflects a real health concern.</summary>
    public int Confidence { get; init; }

    public string ConfidenceLabel => Confidence switch
    {
        >= 75 => "High",
        >= 50 => "Medium",
        _ => "Low"
    };

    public int SourceCount { get; init; } = 1;

    public IReadOnlyList<Guid> RelatedNewsArticleIds { get; init; } = [];

    public bool IsContradicted { get; init; }

    public string VerificationLabel => "Unconfirmed";
}
