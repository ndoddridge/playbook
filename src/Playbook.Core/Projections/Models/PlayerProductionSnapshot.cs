using Playbook.Core.Players;

namespace Playbook.Core.Projections.Models;

/// <summary>
/// Player-specific production inputs for projection baselines.
/// Prefer curated/live season stats; attribute fallbacks are explicitly labeled.
/// </summary>
public sealed class PlayerProductionSnapshot
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Position Position { get; init; }

    public required int Season { get; init; }

    public required ProductionDataSource Source { get; init; }

    /// <summary>Human-readable origin shown in projection reasoning.</summary>
    public required string SourceDescription { get; init; }

    public int GamesPlayed { get; init; }

    public int PassingYards { get; init; }

    public int PassingTouchdowns { get; init; }

    public int Interceptions { get; init; }

    public int RushingAttempts { get; init; }

    public int RushingYards { get; init; }

    public int RushingTouchdowns { get; init; }

    public int Targets { get; init; }

    public int Receptions { get; init; }

    public int ReceivingYards { get; init; }

    public int ReceivingTouchdowns { get; init; }

    /// <summary>Weekly prior for K/DST (or other positions without box-score components).</summary>
    public decimal? SpecialistWeeklyPrior { get; init; }
}

public enum ProductionDataSource
{
    /// <summary>Curated player-specific historical/season production.</summary>
    CuratedSeason = 0,

    /// <summary>Taken from <see cref="SeasonStats"/> on the player profile.</summary>
    ProfileSeason = 1,

    /// <summary>
    /// No box-score stats available — documented fallback using position + YearsPro + Age + Status.
    /// </summary>
    AttributeFallback = 2
}
