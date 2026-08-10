namespace Playbook.Core.Stats.Models;

/// <summary>
/// Normalized weekly / game-level statistics. Retained for trend, usage, consistency, and volatility.
/// </summary>
public sealed class PlayerGameStats
{
    public required Guid PlayerId { get; init; }

    public required int Season { get; init; }

    public required int Week { get; init; }

    public required string SeasonType { get; init; }

    public FootballLevel Level { get; init; } = FootballLevel.Nfl;

    public string? OpponentTeam { get; init; }

    public string? Team { get; init; }

    public string? Position { get; init; }

    public int? PassAttempts { get; init; }
    public int? PassCompletions { get; init; }
    public int? PassYards { get; init; }
    public int? PassTouchdowns { get; init; }
    public int? PassInterceptions { get; init; }

    public int? RushAttempts { get; init; }
    public int? RushYards { get; init; }
    public int? RushTouchdowns { get; init; }

    public int? Targets { get; init; }
    public int? Receptions { get; init; }
    public int? ReceivingYards { get; init; }
    public int? ReceivingTouchdowns { get; init; }

    public int? Fumbles { get; init; }

    public string? SourceProvider { get; init; }

    public string? Source { get; init; }

    public StatsIdentityMatch IdentityMatch { get; init; } = StatsIdentityMatch.Matched;

    public IReadOnlyList<string> MissingFields { get; init; } = [];

    public DateTimeOffset LastUpdated { get; init; }

    public bool HasAnyCountingStat =>
        PassAttempts is > 0 ||
        PassYards is > 0 ||
        RushAttempts is > 0 ||
        RushYards is > 0 ||
        Targets is > 0 ||
        Receptions is > 0 ||
        ReceivingYards is > 0 ||
        PassTouchdowns is > 0 ||
        RushTouchdowns is > 0 ||
        ReceivingTouchdowns is > 0;

    public CanonicalCountingStats ToCountingStats() => new()
    {
        PassAttempts = PassAttempts,
        PassCompletions = PassCompletions,
        PassYards = PassYards,
        PassTouchdowns = PassTouchdowns,
        PassInterceptions = PassInterceptions,
        RushAttempts = RushAttempts,
        RushYards = RushYards,
        RushTouchdowns = RushTouchdowns,
        Targets = Targets,
        Receptions = Receptions,
        ReceivingYards = ReceivingYards,
        ReceivingTouchdowns = ReceivingTouchdowns,
        Fumbles = Fumbles
    };
}
