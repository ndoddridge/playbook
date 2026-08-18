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
    /// <summary>Imported market snapshots for this league/season. They are never inferred from current ADP.</summary>
    public IReadOnlyList<HistoricalAdpSnapshot> HistoricalAdpSnapshots { get; init; } = [];
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
public enum HistoricalSourceQuality { Unknown, Community, Established, Primary }
public enum HistoricalAvailabilityAssessment { Unknown, High, Medium, Low }
public enum HistoricalMarketValueClassification { Unavailable, MajorReach, Reach, Market, Value, MajorValue }

public sealed class HistoricalAdpSnapshot
{
    public required string LeagueId { get; init; }
    public required string Season { get; init; }
    public required string Source { get; init; }
    /// <summary>Publisher/export timestamp. A snapshot without it is retained nowhere and cannot evaluate a pick.</summary>
    public required DateTimeOffset SnapshotDateUtc { get; init; }
    public required LeagueType LeagueType { get; init; }
    /// <summary>Explicit source label such as PPR, HalfPPR, or Standard. Never silently inferred from another format.</summary>
    public required string ScoringFormat { get; init; }
    public int? TeamCount { get; init; }
    public HistoricalSourceQuality SourceQuality { get; init; } = HistoricalSourceQuality.Unknown;
    public required IReadOnlyList<HistoricalAdpRecord> Players { get; init; }
}

public sealed class HistoricalAdpRecord
{
    public string? SleeperPlayerId { get; init; }
    public Guid? PlaybookPlayerId { get; init; }
    public required string PlayerName { get; init; }
    public required string Position { get; init; }
    public int? OverallRank { get; init; }
    public int? PositionRank { get; init; }
    public double? Adp { get; init; }
}

public sealed record HistoricalPickMarketComparison(
    string HistoricalDraftId, int PickNumber, string PlayerKey, double? Adp, int? OverallRank,
    int? PositionRank, DateTimeOffset? SnapshotDateUtc, HistoricalMarketValueClassification Classification,
    string? UnavailableReason);

public sealed record HistoricalLeaguePlayerRange(
    string PlayerKey, string PlayerName, string Position, int DraftCount, int SeasonCount,
    int MinimumPick, double MedianPick, double AveragePick, int MaximumPick, int DistinctOwnerCount,
    IReadOnlyList<string> OwnerKeys, IReadOnlyList<int> RecentPicks, HistoricalEvidenceStrength EvidenceStrength,
    IReadOnlyDictionary<string, int>? OwnerSelections = null);

public enum HistoricalTargetTiming { Unknown, Early, SafeToWait, LastSafePick, TooLateLikelyGone }
public enum HistoricalTargetRecommendation { Unknown, TakeNow, LikelyToSurvive, ProbablySurvives, Wait }
public sealed record HistoricalTargetTimingAssessment(
    HistoricalTargetTiming Timing, HistoricalTargetRecommendation Recommendation,
    HistoricalAvailabilityResult Availability, bool ElevatedOwnerRisk, bool PositionRun,
    string Explanation);

public sealed record HistoricalAvailabilityResult(
    string PlayerKey, int CurrentPick, int NextPick, HistoricalAvailabilityAssessment Assessment,
    HistoricalLeaguePlayerRange? LeagueRange, HistoricalAdpRecord? HistoricalAdp,
    HistoricalEvidenceStrength EvidenceStrength, string Explanation);

public sealed record HistoricalTargetPlayerQuery(
    HistoricalLeaguePlayerRange? LeagueRange, HistoricalAdpRecord? HistoricalAdp,
    IReadOnlyList<HistoricalPickMarketComparison> PickComparisons, HistoricalAvailabilityResult Availability);

public sealed record HistoricalLeaguePositionTendency(
    string Position, int DraftCount, int? FirstSelectedPick, double AveragePick,
    IReadOnlyDictionary<int, int> SelectionsByRound, HistoricalEvidenceStrength EvidenceStrength);

public sealed record HistoricalOwnerMarketSignal(
    string OwnerKey, string OwnerName, string Position, int SelectionCount, int SeasonCount,
    double? AveragePick, double? AverageAdpDelta, HistoricalEvidenceStrength EvidenceStrength);

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
    IReadOnlyList<string> Warnings, HistoricalLeagueDraft? Draft = null,
    Playbook.Core.Draft.PersonalDraftKnowledge? PersonalKnowledge = null);
