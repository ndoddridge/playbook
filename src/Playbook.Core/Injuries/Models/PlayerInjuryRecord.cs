namespace Playbook.Core.Injuries.Models;

/// <summary>
/// Normalized injury / availability record. Missing fields stay null — never fabricated.
/// </summary>
public sealed record PlayerInjuryRecord
{
    public required Guid PlayerId { get; init; }

    public required DateTimeOffset Date { get; init; }

    public int? Season { get; init; }

    /// <summary>NFL vs College when known. Null when the source does not say.</summary>
    public InjuryCompetitionLevel? Level { get; init; }

    public string? Team { get; init; }

    public string? BodyPart { get; init; }

    /// <summary>Free-text injury type from the source (sprain, tear, etc.) when provided.</summary>
    public string? InjuryType { get; init; }

    public string? Description { get; init; }

    /// <summary>Canonical status string (Out, Questionable, IR, Active/Returned, etc.).</summary>
    public required string Status { get; init; }

    public int? GamesMissed { get; init; }

    public string? PracticeStatus { get; init; }

    public string? GameStatus { get; init; }

    public InjurySeverity? Severity { get; init; }

    public string? Source { get; init; }

    public string? SourceUrl { get; init; }

    /// <summary>True for official provider designations; false for speculative rows (should be rare here).</summary>
    public bool Verified { get; init; } = true;

    public DateTimeOffset LastUpdated { get; init; }

    /// <summary>True when this is the player's latest known injury designation.</summary>
    public bool IsCurrent { get; init; }

    public string? ExternalId { get; init; }

    public string VerificationLabel =>
        IsCurrent && Verified ? "Current" :
        Verified ? "Verified" :
        "Unconfirmed";
}
