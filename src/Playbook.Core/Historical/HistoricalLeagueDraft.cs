using Playbook.Core.Leagues;

namespace Playbook.Core.Historical;

/// <summary>Immutable-in-spirit, persisted record of one completed or partial historical draft.</summary>
public sealed class HistoricalLeagueDraft
{
    public required string HistoricalDraftId { get; init; }
    public required string LeagueId { get; init; }
    public required string Season { get; init; }
    public required string LeagueName { get; init; }
    public required LeagueType LeagueType { get; init; }
    public required string DraftType { get; init; }
    public required int TeamCount { get; init; }
    public required int RoundCount { get; init; }
    public required IReadOnlyDictionary<string, double> ScoringSettings { get; init; }
    public required IReadOnlyList<string> RosterSettings { get; init; }
    public required IReadOnlyList<HistoricalOwner> Owners { get; init; }
    public required IReadOnlyList<HistoricalDraftPick> Picks { get; init; }
    public DateTimeOffset? DraftedAtUtc { get; init; }
    public string Source { get; init; } = "import";
    public bool IsComplete { get; init; }
    public DateTimeOffset ImportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class HistoricalOwner
{
    /// <summary>Stable platform ID when supplied. Display names are intentionally never used as a merge key.</summary>
    public string? SleeperUserId { get; init; }
    public required string DisplayName { get; init; }
    public int? RosterId { get; init; }
}

public sealed class HistoricalDraftPick
{
    public required int PickNumber { get; init; }
    public required int Round { get; init; }
    public required int DraftSlot { get; init; }
    public required string OwnerKey { get; init; }
    public required string OwnerName { get; init; }
    public string? SleeperUserId { get; init; }
    public int? RosterId { get; init; }
    public string? SleeperPlayerId { get; init; }
    public Guid? PlaybookPlayerId { get; init; }
    public required string PlayerName { get; init; }
    public required string Position { get; init; }
    public string? NflTeam { get; init; }
    public DateTimeOffset? PickedAtUtc { get; init; }
    public bool IsKeeper { get; init; }
    /// <summary>Roster position counts immediately before this selection, reconstructed only from prior draft picks.</summary>
    public IReadOnlyDictionary<string, int> RosterBefore { get; init; } = new Dictionary<string, int>();
    // Deliberately nullable: current projections/ADP must never be backfilled as historical fact.
    public double? HistoricalAdp { get; init; }
    public double? HistoricalProjection { get; init; }
    public int? HistoricalOverallRank { get; init; }
    public int? HistoricalPositionRank { get; init; }
}

public enum HistoricalEvidenceStrength { Unavailable, Insufficient, Limited, Moderate, Strong }

public sealed record HistoricalOwnerTendency(
    string LeagueId, string OwnerKey, string OwnerName, LeagueType LeagueType,
    string Position, int Round, int SelectionCount, int SeasonCount,
    HistoricalEvidenceStrength EvidenceStrength);

public sealed record HistoricalPlayerHistory(
    string PlayerKey, int DraftCount, int SeasonCount, int? EarliestPick,
    int? LatestPick, IReadOnlyList<string> OwnerKeys, HistoricalEvidenceStrength EvidenceStrength);

public sealed record HistoricalPositionDraftRange(
    string LeagueId, LeagueType LeagueType, string Position, int SelectionCount,
    int? EarliestPick, int? LatestPick, HistoricalEvidenceStrength EvidenceStrength);

public sealed record HistoricalImportResult(bool Succeeded, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, HistoricalLeagueDraft? Draft = null);
