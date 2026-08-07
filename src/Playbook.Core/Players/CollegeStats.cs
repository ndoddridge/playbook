namespace Playbook.Core.Players;

/// <summary>
/// Placeholder college statistics for prospect / historical context.
/// </summary>
public sealed class CollegeStats
{
    public string? School { get; init; }

    public int? Seasons { get; init; }

    public int? GamesPlayed { get; init; }

    public decimal? FantasyPointsEquivalent { get; init; }

    public string? NotableNote { get; init; }
}
