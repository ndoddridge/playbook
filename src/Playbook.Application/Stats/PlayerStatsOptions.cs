using Playbook.Application.Stats.Interfaces;

namespace Playbook.Application.Stats;

public enum PlayerStatsProviderKind
{
    Mock = 0,
    Live = 1
}

public sealed class PlayerStatsOptions
{
    public const string SectionName = "PlayerStats";

    public PlayerStatsProviderKind Provider { get; set; } = PlayerStatsProviderKind.Live;

    /// <summary>Historical NFL game/season provider for completed seasons.</summary>
    public HistoricalPlayerStatsProviderKind HistoricalProvider { get; set; } =
        HistoricalPlayerStatsProviderKind.Nflverse;

    /// <summary>How many completed NFL seasons to sync (in addition to the current season).</summary>
    public int HistoricalSeasonCount { get; set; } = 5;

    /// <summary>How many seasons of game logs to retain in the store (granular weekly data).</summary>
    public int GameLogSeasonCount { get; set; } = 3;

    /// <summary>Relative path under the content/data root for the JSON season cache.</summary>
    public string CacheFileName { get; set; } = "player-stats-cache.json";

    /// <summary>Relative path for game-log JSON cache.</summary>
    public string GameLogCacheFileName { get; set; } = "player-game-logs-cache.json";

    /// <summary>Reuse local cache when younger than this many minutes.</summary>
    public int CacheTtlMinutes { get; set; } = 360;

    public int TimeoutSeconds { get; set; } = 120;
}
