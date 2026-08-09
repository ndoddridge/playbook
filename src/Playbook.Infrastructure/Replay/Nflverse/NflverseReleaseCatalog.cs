namespace Playbook.Infrastructure.Replay.Nflverse;

/// <summary>Official nflverse-data GitHub release URLs used by the historical adapter.</summary>
public static class NflverseReleaseCatalog
{
    public const string PlayerStatsUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/player_stats/player_stats_{0}.csv.gz";

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
