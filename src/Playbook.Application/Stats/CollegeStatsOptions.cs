namespace Playbook.Application.Stats;

public enum CollegeStatsProviderKind
{
    Mock = 0,
    Live = 1
}

public sealed class CollegeStatsOptions
{
    public const string SectionName = "CollegeStats";

    public CollegeStatsProviderKind Provider { get; set; } = CollegeStatsProviderKind.Live;

    /// <summary>Relative path under the content/data root for the college JSON cache.</summary>
    public string CacheFileName { get; set; } = "college-stats-cache.json";

    /// <summary>Reuse local college cache when younger than this many minutes.</summary>
    public int CacheTtlMinutes { get; set; } = 1440;

    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>Max concurrent ESPN athlete requests during a college sync.</summary>
    public int MaxConcurrency { get; set; } = 8;

    /// <summary>Upper bound of young players to fetch college stats for in one sync.</summary>
    public int MaxAthletesPerSync { get; set; } = 400;

    /// <summary>Promote college rows into the Career season selector when YearsPro is below this.</summary>
    public int CareerPromotionYearsProThreshold { get; set; } = 3;
}
