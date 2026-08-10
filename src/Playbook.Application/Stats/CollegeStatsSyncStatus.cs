namespace Playbook.Application.Stats;

public interface ICollegeStatsSyncStatus
{
    string ConfiguredProvider { get; }

    string ActiveProvider { get; }

    int CollegePlayersLoaded { get; }

    int CollegeSeasonsLoaded { get; }

    DateTimeOffset? LastCollegeSync { get; }

    TimeSpan? CollegeSyncRuntime { get; }

    string? CollegeSyncError { get; }

    bool UsedFallback { get; }

    bool UsedCache { get; }
}

public sealed class CollegeStatsSyncStatus : ICollegeStatsSyncStatus
{
    private readonly object _gate = new();

    public string ConfiguredProvider { get; private set; } = CollegeStatsProviderKind.Mock.ToString();

    public string ActiveProvider { get; private set; } = CollegeStatsProviderKind.Mock.ToString();

    public int CollegePlayersLoaded { get; private set; }

    public int CollegeSeasonsLoaded { get; private set; }

    public DateTimeOffset? LastCollegeSync { get; private set; }

    public TimeSpan? CollegeSyncRuntime { get; private set; }

    public string? CollegeSyncError { get; private set; }

    public bool UsedFallback { get; private set; }

    public bool UsedCache { get; private set; }

    public void SetConfigured(CollegeStatsProviderKind kind)
    {
        lock (_gate)
        {
            ConfiguredProvider = kind.ToString();
        }
    }

    public void RecordSuccess(
        CollegeStatsProviderKind active,
        int playersLoaded,
        int seasonsLoaded,
        TimeSpan runtime,
        bool usedFallback,
        bool usedCache,
        string? priorError)
    {
        lock (_gate)
        {
            ActiveProvider = active.ToString();
            CollegePlayersLoaded = playersLoaded;
            CollegeSeasonsLoaded = seasonsLoaded;
            CollegeSyncRuntime = runtime;
            LastCollegeSync = DateTimeOffset.Now;
            UsedFallback = usedFallback;
            UsedCache = usedCache;
            CollegeSyncError = priorError;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            CollegeSyncError = error;
        }
    }
}
