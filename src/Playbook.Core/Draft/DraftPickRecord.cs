namespace Playbook.Core.Draft;

/// <summary>One real Sleeper draft pick, made or not-yet-made. Read-only — Playbook never writes
/// picks back to Sleeper.</summary>
public sealed class DraftPickRecord
{
    public required int PickNumber { get; init; }

    public required int Round { get; init; }

    /// <summary>1-indexed draft position within the round.</summary>
    public required int DraftSlot { get; init; }

    /// <summary>Null until Sleeper resolves ownership for this slot (should always be known once a
    /// draft has real rosters attached).</summary>
    public int? RosterId { get; init; }

    /// <summary>Null until the pick is actually made.</summary>
    public Guid? PlayerId { get; init; }

    public string? PlayerName { get; init; }

    public string? PositionLabel { get; init; }

    /// <summary>True when Sleeper reports this as a pre-assigned keeper pick, not a live draft pick.</summary>
    public bool IsKeeper { get; init; }

    public bool IsMade => PlayerId is not null;
}
