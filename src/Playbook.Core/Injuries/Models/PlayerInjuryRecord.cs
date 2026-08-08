namespace Playbook.Core.Injuries.Models;

/// <summary>
/// Normalized injury / availability record. Missing fields stay null — never fabricated.
/// </summary>
public sealed record PlayerInjuryRecord
{
    public required Guid PlayerId { get; init; }

    public required DateTimeOffset Date { get; init; }

    /// <summary>Canonical status string (Out, Questionable, IR, Active/Returned, etc.).</summary>
    public required string Status { get; init; }

    public string? BodyPart { get; init; }

    public string? Description { get; init; }

    public string? PracticeStatus { get; init; }

    public string? GameStatus { get; init; }

    public string? Source { get; init; }

    public string? SourceUrl { get; init; }

    public int? Season { get; init; }

    public DateTimeOffset LastUpdated { get; init; }

    /// <summary>True when this is the player's latest known injury designation.</summary>
    public bool IsCurrent { get; init; }

    public string? ExternalId { get; init; }
}
