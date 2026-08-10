namespace Playbook.Core.Injuries.Models;

/// <summary>
/// Aggregated career injury view for one player. Distinguishes current, historical, and unconfirmed.
/// </summary>
public sealed class PlayerInjuryProfile
{
    public required Guid PlayerId { get; init; }

    public CurrentInjuryDataStatus CurrentDataStatus { get; init; }

    public string? CurrentStatus { get; init; }

    public PlayerInjuryRecord? CurrentInjury { get; init; }

    public string? PracticeStatus { get; init; }

    public string? GameStatus { get; init; }

    /// <summary>Recent injuries that may still affect current evaluation (High/Moderate relevance).</summary>
    public IReadOnlyList<InjuryHistoryEntry> RecentHistory { get; init; } = [];

    /// <summary>Full available NFL injury history (chronological, newest first).</summary>
    public IReadOnlyList<InjuryHistoryEntry> NflCareerHistory { get; init; } = [];

    /// <summary>Full available college injury history (chronological, newest first).</summary>
    public IReadOnlyList<InjuryHistoryEntry> CollegeHistory { get; init; } = [];

    /// <summary>All historical entries with relevance (flat, for intelligence).</summary>
    public IReadOnlyList<InjuryHistoryEntry> HistoricalEntries { get; init; } = [];

    /// <summary>Legacy flat historical records (without relevance wrappers).</summary>
    public IReadOnlyList<PlayerInjuryRecord> HistoricalRecords { get; init; } = [];

    public HistoricalDataStatus HistoricalDataStatus { get; init; }

    public HistoricalDataStatus NflHistoricalDataStatus { get; init; }

    public HistoricalDataStatus CollegeHistoricalDataStatus { get; init; }

    public IReadOnlyList<UnconfirmedInjurySignal> UnconfirmedSignals { get; init; } = [];

    public string? RiskSummary { get; init; }

    public DateTimeOffset? LastUpdated { get; init; }

    public IReadOnlyList<string> SupportingSources { get; init; } = [];

    public string? HistoricalAvailabilityMessage { get; init; }

    public string? CollegeAvailabilityMessage { get; init; }

    public string? ProviderCoverage { get; init; }
}
