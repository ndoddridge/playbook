namespace Playbook.Core.Players;

/// <summary>
/// Lightweight trend signal for a player.
/// </summary>
public sealed class PlayerTrend
{
    public required TrendDirection Direction { get; init; }

    public required string Label { get; init; }

    public string? Detail { get; init; }
}
