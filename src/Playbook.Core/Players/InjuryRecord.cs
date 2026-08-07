namespace Playbook.Core.Players;

/// <summary>
/// Single injury / availability record.
/// </summary>
public sealed class InjuryRecord
{
    public required DateOnly ReportedOn { get; init; }

    public required string Description { get; init; }

    public required InjuryStatus Status { get; init; }

    public string? BodyPart { get; init; }

    public DateOnly? ExpectedReturn { get; init; }
}
