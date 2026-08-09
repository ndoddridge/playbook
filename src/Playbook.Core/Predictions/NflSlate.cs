namespace Playbook.Core.Predictions;

/// <summary>
/// A concrete NFL slate backed by real games/events (never invented).
/// Shared by Quick Picks retrieval, filtering, navigation, and intelligence scoping.
/// </summary>
public sealed class NflSlate
{
    public required NflWeekRef Ref { get; init; }

    public required IReadOnlyList<FootballEvent> Events { get; init; }

    public DateTimeOffset EarliestKickoff { get; init; }

    public DateTimeOffset LatestKickoff { get; init; }

    public int EventCount => Events.Count;

    public string DisplayLabel => Ref.DisplayLabel;

    /// <summary>True when every game in the slate is past the completion buffer.</summary>
    public bool IsComplete(DateTimeOffset utcNow, TimeSpan? completionBuffer = null)
    {
        if (Events.Count == 0)
        {
            return true;
        }

        var buffer = completionBuffer ?? TimeSpan.FromHours(4);
        return LatestKickoff + buffer < utcNow;
    }

    /// <summary>True when at least one game has not finished yet.</summary>
    public bool HasUpcomingOrLive(DateTimeOffset utcNow, TimeSpan? completionBuffer = null) =>
        !IsComplete(utcNow, completionBuffer);
}
