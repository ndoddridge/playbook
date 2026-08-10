namespace Playbook.Core.Stats.Models;

/// <summary>
/// Normalized season statistics for one player.
/// Null means unknown/missing. Zero means the player recorded zero. Never silently substitute.
/// Fantasy points are derived from league scoring — stored provider/computed values are optional convenience only.
/// </summary>
public sealed class PlayerSeasonStats
{
    public required Guid PlayerId { get; init; }

    public required int Season { get; init; }

    /// <summary>regular / post / pre / college when known.</summary>
    public required string SeasonType { get; init; }

    public required StatsPeriod Period { get; init; }

    /// <summary>NFL vs College vs Career aggregate. College is never rolled into NFL career samples.</summary>
    public FootballLevel Level { get; init; } = FootballLevel.Nfl;

    public int? Games { get; init; }

    public int? Starts { get; init; }

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

    /// <summary>
    /// Optional convenience fantasy totals. Prefer <c>LeagueFantasyScoring</c> from canonical counting stats.
    /// </summary>
    public decimal? FantasyPointsStandard { get; init; }
    public decimal? FantasyPointsHalfPpr { get; init; }
    public decimal? FantasyPointsPpr { get; init; }

    /// <summary>College school name when <see cref="Period"/> is College.</summary>
    public string? CollegeSchool { get; init; }

    public string? SourceProvider { get; init; }

    public string? Source { get; init; }

    public StatsCompleteness Completeness { get; init; } = StatsCompleteness.Partial;

    public StatsIdentityMatch IdentityMatch { get; init; } = StatsIdentityMatch.NotApplicable;

    /// <summary>Field names that are missing (null), not zero.</summary>
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
        ReceivingTouchdowns is > 0 ||
        FantasyPointsPpr is > 0 ||
        FantasyPointsHalfPpr is > 0 ||
        FantasyPointsStandard is > 0;

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

public enum StatsPeriod
{
    CompletedSeason = 0,
    CurrentSeason = 1,
    College = 2,
    /// <summary>NFL-only career aggregate — never includes college production.</summary>
    Career = 3,
    Weekly = 4
}
