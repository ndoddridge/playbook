namespace Playbook.Infrastructure.Replay.Nflverse;

/// <summary>Official nflverse-data GitHub release URLs used by the historical adapter.</summary>
public static class NflverseReleaseCatalog
{
    /// <summary>
    /// LEGACY player-stats release. Still serves seasons up to 2024 but 404s for 2025 onward —
    /// nflverse renamed the release to stats_player. Retained because the historical replay layer
    /// depends on it for older seasons; new work should use <see cref="WeeklyPlayerStatsUrl"/>.
    /// </summary>
    public const string PlayerStatsUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/player_stats/player_stats_{0}.csv.gz";

    /// <summary>
    /// Current weekly player-stats release. Verified to serve 2019 through 2025, unlike the
    /// legacy path above.
    /// </summary>
    public const string WeeklyPlayerStatsUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/stats_player/stats_player_week_{0}.csv.gz";

    public const string InjuriesUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/injuries/injuries_{0}.csv";

    public const string WeeklyRostersUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/weekly_rosters/roster_weekly_{0}.csv";

    public const string SnapCountsUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/snap_counts/snap_counts_{0}.csv.gz";

    public const string DepthChartsUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/depth_charts/depth_charts_{0}.csv";

    public const string SchedulesUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/schedules/games.csv";

    public static string CacheRoot => Path.Combine(AppContext.BaseDirectory, "data", "nflverse-historical");
}
