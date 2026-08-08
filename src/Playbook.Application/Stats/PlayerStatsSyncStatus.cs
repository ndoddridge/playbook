namespace Playbook.Application.Stats;

public interface IPlayerStatsSyncStatus
{
    string ConfiguredProvider { get; }

    string ActiveProvider { get; }

    int PlayersWithStats { get; }

    int SeasonsLoaded { get; }

    int CurrentSeasonRecords { get; }

    int HistoricalRecords { get; }

    int CollegeRecords { get; }

    DateTimeOffset? LastStatsSync { get; }

    TimeSpan? StatsSyncRuntime { get; }

    string? LastStatsError { get; }

    bool UsedFallback { get; }

    bool UsedCache { get; }
}

public sealed class PlayerStatsSyncStatus : IPlayerStatsSyncStatus
{
    private readonly object _gate = new();

    public string ConfiguredProvider { get; private set; } = PlayerStatsProviderKind.Mock.ToString();

    public string ActiveProvider { get; private set; } = PlayerStatsProviderKind.Mock.ToString();

    public int PlayersWithStats { get; private set; }

    public int SeasonsLoaded { get; private set; }

    public int CurrentSeasonRecords { get; private set; }

    public int HistoricalRecords { get; private set; }

    public int CollegeRecords { get; private set; }

    public DateTimeOffset? LastStatsSync { get; private set; }

    public TimeSpan? StatsSyncRuntime { get; private set; }

    public string? LastStatsError { get; private set; }

    public bool UsedFallback { get; private set; }

    public bool UsedCache { get; private set; }

    public void SetConfigured(PlayerStatsProviderKind kind)
    {
        lock (_gate)
        {
            ConfiguredProvider = kind.ToString();
        }
    }

    public void RecordSuccess(
        PlayerStatsProviderKind active,
        int playersWithStats,
        int seasonsLoaded,
        int currentSeasonRecords,
        int historicalRecords,
        int collegeRecords,
        TimeSpan runtime,
        bool usedFallback,
        bool usedCache,
        string? priorError)
    {
        lock (_gate)
        {
            ActiveProvider = active.ToString();
            PlayersWithStats = playersWithStats;
            SeasonsLoaded = seasonsLoaded;
            CurrentSeasonRecords = currentSeasonRecords;
            HistoricalRecords = historicalRecords;
            CollegeRecords = collegeRecords;
            StatsSyncRuntime = runtime;
            LastStatsSync = DateTimeOffset.Now;
            UsedFallback = usedFallback;
            UsedCache = usedCache;
            LastStatsError = priorError;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            LastStatsError = error;
        }
    }
}
