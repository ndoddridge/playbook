namespace Playbook.Core.Projections.Models;

/// <summary>
/// Optional matchup influence. When unavailable, projections continue without fabrication.
/// </summary>
public sealed class MatchupContext
{
    public required bool IsAvailable { get; init; }

    public string? OpponentTeam { get; init; }

    /// <summary>Relative defensive strength vs this position (−1 soft … +1 tough). Null when unknown.</summary>
    public decimal? OpponentDefenseStrength { get; init; }

    /// <summary>Relative fantasy points allowed to this position vs league average. Null when unknown.</summary>
    public decimal? OpponentPositionPerformance { get; init; }

    public string? Summary { get; init; }

    public static MatchupContext Unavailable(string reason = "Matchup data not yet available") =>
        new()
        {
            IsAvailable = false,
            Summary = reason
        };
}
