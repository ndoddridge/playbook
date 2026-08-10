using Playbook.Core.Leagues;
using Playbook.Core.Players;

namespace Playbook.Core.Replay;

/// <summary>
/// Immutable information state at a historical point in time.
/// Contains ONLY facts known at or before <see cref="InformationCutoff"/>.
/// Actual week outcomes must never live here — see <see cref="HistoricalWeekOutcomes"/>.
/// </summary>
public sealed class HistoricalSnapshot
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required DateTimeOffset InformationCutoff { get; init; }

    public required ScoringType ScoringType { get; init; }

    public required string LeagueName { get; init; }

    public Guid? LeagueId { get; init; }

    public int? SelectedRosterId { get; init; }

    public string? TeamName { get; init; }

    public required IReadOnlyList<HistoricalPlayerState> Players { get; init; }

    public required IReadOnlyList<HistoricalRosterSlot> Roster { get; init; }

    /// <summary>Optional opponent roster ids for matchup-aware replay (may be empty).</summary>
    public IReadOnlyList<HistoricalRosterSlot> OpponentRoster { get; init; } = [];

    public IReadOnlyList<string> UnavailableSources { get; init; } = [];

    public string SourceLabel { get; init; } = "historical-fixture";
}

/// <summary>
/// Player state visible at the information cutoff. Missing fields stay null/unavailable.
/// </summary>
public sealed class HistoricalPlayerState
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Position Position { get; init; }

    public string? Team { get; init; }

    public decimal? ProjectedPoints { get; init; }

    public decimal? Floor { get; init; }

    public decimal? Ceiling { get; init; }

    public int? ProjectionConfidence { get; init; }

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

    /// <summary>Explicit gaps — do not invent replacements.</summary>
    public IReadOnlyList<string> UnavailableSignals { get; init; } = [];
}

public sealed class HistoricalRosterSlot
{
    public required Guid PlayerId { get; init; }

    public required bool IsStarter { get; init; }
}

/// <summary>
/// Actual fantasy outcomes revealed AFTER decisions are recorded.
/// Must never be merged into <see cref="HistoricalSnapshot"/> before decision synthesis.
/// </summary>
public sealed class HistoricalWeekOutcomes
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required ScoringType ScoringType { get; init; }

    public required IReadOnlyDictionary<Guid, HistoricalPlayerOutcome> ByPlayerId { get; init; }
}

public sealed class HistoricalPlayerOutcome
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required double ActualFantasyPoints { get; init; }

    /// <summary>Optional raw note for audits (e.g. game script).</summary>
    public string? Note { get; init; }
}
