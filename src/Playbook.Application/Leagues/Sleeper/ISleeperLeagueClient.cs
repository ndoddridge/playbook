namespace Playbook.Application.Leagues.Sleeper;

public interface ISleeperLeagueClient
{
    Task<SleeperLeagueSnapshot?> GetLeagueSnapshotAsync(
        string leagueId,
        CancellationToken cancellationToken = default);

    /// <summary>Every draft Sleeper has on file for this league (usually one per season).</summary>
    Task<IReadOnlyList<SleeperDraftSummary>> GetDraftsForLeagueAsync(
        string leagueId,
        CancellationToken cancellationToken = default);

    /// <summary>Draft metadata/settings/order — never includes picks.</summary>
    Task<SleeperDraftSnapshot?> GetDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default);

    /// <summary>Every pick made so far, in pick-number order. Empty list before the draft starts.</summary>
    Task<IReadOnlyList<SleeperDraftPickSnapshot>> GetDraftPicksAsync(
        string draftId,
        CancellationToken cancellationToken = default);
}

public sealed class SleeperDraftSummary
{
    public required string DraftId { get; init; }
    public required string Status { get; init; }
    public required string Season { get; init; }
    public long? StartTime { get; init; }
}

public sealed class SleeperDraftSnapshot
{
    public required string DraftId { get; init; }
    public required string LeagueId { get; init; }
    public required string Season { get; init; }

    /// <summary>Real Sleeper values: pre_draft / drafting / paused / complete.</summary>
    public required string Status { get; init; }

    /// <summary>Real Sleeper values: "snake" or "linear".</summary>
    public required string Type { get; init; }

    public required int Rounds { get; init; }
    public required int Teams { get; init; }

    /// <summary>Sleeper user id → draft slot (1-indexed). Combine with league roster ownership to
    /// resolve slot → roster id; Sleeper does not always publish slot_to_roster_id directly.</summary>
    public required IReadOnlyDictionary<string, int> DraftOrderByUserId { get; init; }

    /// <summary>
    /// Draft slot → roster id, published directly on the draft object. Preferred over
    /// cross-referencing league rosters because it also works for a draft attached by id, where
    /// no league roster list is available. Empty when Sleeper omits it.
    /// </summary>
    public IReadOnlyDictionary<int, int> SlotToRosterId { get; init; } =
        new Dictionary<int, int>();

    /// <summary>
    /// Starting roster slots derived from the draft's own settings (slots_qb, slots_rb, ...).
    /// Lets a directly-attached draft describe its own roster shape without a league.
    /// </summary>
    public IReadOnlyList<string> RosterPositions { get; init; } = [];

    /// <summary>Sleeper scoring_type from draft metadata, e.g. "ppr", "2qb", "dynasty_2qb".</summary>
    public string? ScoringType { get; init; }

    /// <summary>Sleeper league_type from draft metadata: "0" redraft, "1" keeper, "2" dynasty.</summary>
    public string? LeagueTypeRaw { get; init; }

    /// <summary>Draft name from metadata, for display when attached by id.</summary>
    public string? Name { get; init; }
}

public sealed class SleeperDraftPickSnapshot
{
    public required int PickNumber { get; init; }
    public required int Round { get; init; }
    public required int DraftSlot { get; init; }
    public int? RosterId { get; init; }
    public string? PickedByUserId { get; init; }
    public string? SleeperPlayerId { get; init; }
    public string? PlayerName { get; init; }
    public string? Position { get; init; }
    public string? NflTeam { get; init; }
    public DateTimeOffset? PickedAtUtc { get; init; }
    public bool IsKeeper { get; init; }
}

public sealed class SleeperLeagueSnapshot
{
    public required string ExternalLeagueId { get; init; }
    public required string Name { get; init; }
    public required string Season { get; init; }
    public required string Status { get; init; }
    public required int TeamCount { get; init; }
    public required int CurrentWeek { get; init; }
    public required int SleeperLeagueType { get; init; }
    public required IReadOnlyDictionary<string, double> ScoringSettings { get; init; }
    public required IReadOnlyList<string> RosterPositions { get; init; }
    public required IReadOnlyList<SleeperRosterSnapshot> Rosters { get; init; }
    public string? PreviousLeagueId { get; init; }
}

public sealed class SleeperRosterSnapshot
{
    public required int RosterId { get; init; }
    public required string? OwnerId { get; init; }
    public required string TeamName { get; init; }
    public required string OwnerName { get; init; }
    public required IReadOnlyList<string> SleeperPlayerIds { get; init; }
    public required IReadOnlyList<string> StarterSleeperPlayerIds { get; init; }
    public required IReadOnlyList<string> ReserveSleeperPlayerIds { get; init; }
    public required IReadOnlyList<string> TaxiSleeperPlayerIds { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Ties { get; init; }
    public double FantasyPoints { get; init; }
}
