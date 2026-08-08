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

    /// <summary>How many completed NFL seasons to sync (in addition to the current season).</summary>
    public int HistoricalSeasonCount { get; set; } = 3;

    /// <summary>Relative path under the content/data root for the JSON cache.</summary>
    public string CacheFileName { get; set; } = "player-stats-cache.json";

    /// <summary>Reuse local cache when younger than this many minutes.</summary>
    public int CacheTtlMinutes { get; set; } = 360;

    public int TimeoutSeconds { get; set; } = 60;
}
