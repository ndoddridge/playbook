namespace Playbook.Core.Stats.Models;

/// <summary>
/// Normalized season statistics for one player. Missing values stay null/zero — never fabricated.
/// </summary>
public sealed class PlayerSeasonStats
{
    public required Guid PlayerId { get; init; }

    public required int Season { get; init; }

    /// <summary>regular / post / pre / college when known.</summary>
    public required string SeasonType { get; init; }

    public required StatsPeriod Period { get; init; }

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

    public decimal? FantasyPointsStandard { get; init; }
    public decimal? FantasyPointsHalfPpr { get; init; }
    public decimal? FantasyPointsPpr { get; init; }

    /// <summary>College school name when <see cref="Period"/> is College.</summary>
    public string? CollegeSchool { get; init; }

    public string? SourceProvider { get; init; }

    public DateTimeOffset LastUpdated { get; init; }

    public bool HasAnyCountingStat =>
        (PassAttempts ?? 0) > 0 ||
        (PassYards ?? 0) > 0 ||
        (RushAttempts ?? 0) > 0 ||
        (RushYards ?? 0) > 0 ||
        (Targets ?? 0) > 0 ||
        (Receptions ?? 0) > 0 ||
        (ReceivingYards ?? 0) > 0 ||
        (FantasyPointsPpr ?? FantasyPointsHalfPpr ?? FantasyPointsStandard ?? 0) > 0;
}

public enum StatsPeriod
{
    CompletedSeason = 0,
    CurrentSeason = 1,
    College = 2
}
