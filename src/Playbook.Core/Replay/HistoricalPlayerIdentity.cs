using Playbook.Core.Players;

namespace Playbook.Core.Replay;

/// <summary>
/// Stable historical player identity. Matching is GSIS-first; display name is never a sole join key.
/// </summary>
public sealed class HistoricalPlayerIdentity
{
    public required Guid PlaybookId { get; init; }

    public required string GsisId { get; init; }

    public string? SleeperId { get; init; }

    public string? EspnId { get; init; }

    public string? YahooId { get; init; }

    public required string FullName { get; init; }

    public required Position Position { get; init; }

    public required string Team { get; init; }

    public required int Season { get; init; }

    public required int Week { get; init; }

    public string? RosterStatus { get; init; }
}
