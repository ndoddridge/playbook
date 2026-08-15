namespace Playbook.Core.Draft;

/// <summary>Current state of one real Sleeper draft — a snapshot as of <see cref="RetrievedAt"/>,
/// never a prediction of picks that haven't happened.</summary>
public sealed class DraftBoard
{
    public required string DraftId { get; init; }

    public required Guid LeagueId { get; init; }

    public required string Season { get; init; }

    public required DraftStatus Status { get; init; }

    /// <summary>Real Sleeper value: "snake" or "linear".</summary>
    public required string Type { get; init; }

    public required int TotalRounds { get; init; }

    public required int TeamCount { get; init; }

    /// <summary>Every made pick, pick-number order. Empty before the draft starts.</summary>
    public required IReadOnlyList<DraftPickRecord> Picks { get; init; }

    /// <summary>Overall pick number for the next pick to be made (Picks.Count + 1).</summary>
    public required int NextPickNumber { get; init; }

    /// <summary>Roster on the clock for <see cref="NextPickNumber"/>, computed from real draft
    /// order + snake/linear settings — never invented once the draft has started.</summary>
    public int? NextRosterId { get; init; }

    /// <summary>The connected league's own roster id, when known.</summary>
    public int? UserRosterId { get; init; }

    public required DateTimeOffset RetrievedAt { get; init; }

    public bool IsUserOnTheClock =>
        Status == DraftStatus.Drafting && UserRosterId is not null && NextRosterId == UserRosterId;

    public bool IsComplete => Status == DraftStatus.Complete || NextPickNumber > TotalRounds * TeamCount;
}
