using Playbook.Core.Leagues;
using Playbook.Core.Replay;

namespace Playbook.Application.Replay;

/// <summary>
/// Loads raw historical material. Implementations may contain future-dated rows;
/// <see cref="IHistoricalSnapshotBuilder"/> is responsible for enforcing the cutoff.
/// </summary>
public interface IHistoricalSnapshotSource
{
    Task<HistoricalRawWeekData?> GetRawWeekAsync(
        int season,
        int week,
        ScoringType scoringType,
        string? fixtureId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Raw week payload before cutoff filtering. May intentionally include future-dated events
/// so leakage tests can prove the builder strips them.
/// </summary>
public sealed class HistoricalRawWeekData
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required DateTimeOffset InformationCutoff { get; init; }

    public required ScoringType ScoringType { get; init; }

    public required string LeagueName { get; init; }

    public Guid? LeagueId { get; init; }

    public int? SelectedRosterId { get; init; }

    public string? TeamName { get; init; }

    public required IReadOnlyList<HistoricalRawPlayerRecord> Players { get; init; }

    public required IReadOnlyList<HistoricalRosterSlot> Roster { get; init; }

    public IReadOnlyList<HistoricalRosterSlot> OpponentRoster { get; init; } = [];

    public required IReadOnlyList<HistoricalPlayerOutcome> Outcomes { get; init; }

    public IReadOnlyList<string> UnavailableSources { get; init; } = [];

    public string SourceLabel { get; init; } = "historical-fixture";
}

/// <summary>
/// Raw player row that may include future-dated observations.
/// Only rows with ObservedAt &lt;= cutoff (or null observed-at treated as pre-known fixture facts)
/// enter the snapshot.
/// </summary>
public sealed class HistoricalRawPlayerRecord
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Core.Players.Position Position { get; init; }

    public string? Team { get; init; }

    public decimal? ProjectedPoints { get; init; }

    public decimal? Floor { get; init; }

    public decimal? Ceiling { get; init; }

    public int? ProjectionConfidence { get; init; }

    public DateTimeOffset? ProjectionObservedAt { get; init; }

    public int? OpportunityScore { get; init; }

    public int? UsageScore { get; init; }

    public string? HealthLabel { get; init; }

    public string? InjuryStatus { get; init; }

    public string? InjuryBodyPart { get; init; }

    public DateTimeOffset? InjuryObservedAt { get; init; }

    public string? RecentNewsHeadline { get; init; }

    public DateTimeOffset? RecentNewsObservedAt { get; init; }

    public bool RecentNewsConfirmed { get; init; }

    public string? RoleNote { get; init; }

    public int? RecentProductionScore { get; init; }

    public IReadOnlyList<string> UnavailableSignals { get; init; } = [];
}
