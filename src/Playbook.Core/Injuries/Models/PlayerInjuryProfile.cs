namespace Playbook.Core.Injuries.Models;

/// <summary>
/// Aggregated injury view for one player. Distinguishes current vs historical availability.
/// </summary>
public sealed class PlayerInjuryProfile
{
    public required Guid PlayerId { get; init; }

    public CurrentInjuryDataStatus CurrentDataStatus { get; init; }

    public string? CurrentStatus { get; init; }

    public PlayerInjuryRecord? CurrentInjury { get; init; }

    public string? PracticeStatus { get; init; }

    public string? GameStatus { get; init; }

    public IReadOnlyList<PlayerInjuryRecord> HistoricalRecords { get; init; } = [];

    public HistoricalDataStatus HistoricalDataStatus { get; init; }

    public string? RiskSummary { get; init; }

    public DateTimeOffset? LastUpdated { get; init; }

    public IReadOnlyList<string> SupportingSources { get; init; } = [];

    public string? HistoricalAvailabilityMessage { get; init; }
}
