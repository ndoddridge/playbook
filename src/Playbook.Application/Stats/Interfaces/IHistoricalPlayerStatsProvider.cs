using Playbook.Core.Stats.Models;

namespace Playbook.Application.Stats.Interfaces;

/// <summary>
/// Historical NFL season + game-log statistics from a durable provider (e.g. nflverse).
/// </summary>
public interface IHistoricalPlayerStatsProvider
{
    HistoricalPlayerStatsProviderKind Kind { get; }

    string DisplayName { get; }

    bool IsConfigured { get; }

    Task<HistoricalPlayerStatsBatch> GetHistoricalStatsAsync(
        HistoricalPlayerStatsSyncRequest request,
        CancellationToken cancellationToken = default);
}

public enum HistoricalPlayerStatsProviderKind
{
    Null = 0,
    Mock = 1,
    Nflverse = 2
}

public sealed class HistoricalPlayerStatsSyncRequest
{
    public required IReadOnlyList<int> Seasons { get; init; }

    public required string SeasonType { get; init; }

    /// <summary>When true, re-download season files even if a local copy exists.</summary>
    public bool ForceRedownload { get; init; }
}

public sealed class HistoricalPlayerStatsBatch
{
    public IReadOnlyList<PlayerSeasonStats> SeasonRecords { get; init; } = [];

    public IReadOnlyList<PlayerGameStats> GameLogs { get; init; } = [];

    public int IdentityMatches { get; init; }

    public int UnresolvedPlayers { get; init; }

    public string? Error { get; init; }

    public TimeSpan ResponseTime { get; init; }
}
