namespace Playbook.Application.Injuries;

public interface IInjurySyncStatus
{
    string ConfiguredProvider { get; }

    string ActiveProvider { get; }

    int PlayersWithInjuryData { get; }

    int InjuryRecordsLoaded { get; }

    int CurrentInjuryRecords { get; }

    int HistoricalInjuryRecords { get; }

    DateTimeOffset? LastInjurySync { get; }

    TimeSpan? InjurySyncRuntime { get; }

    string? LastInjuryError { get; }

    bool UsedFallback { get; }

    bool UsedCache { get; }
}

public sealed class InjurySyncStatus : IInjurySyncStatus
{
    private readonly object _gate = new();

    public string ConfiguredProvider { get; private set; } = InjuryProviderKind.Mock.ToString();

    public string ActiveProvider { get; private set; } = InjuryProviderKind.Mock.ToString();

    public int PlayersWithInjuryData { get; private set; }

    public int InjuryRecordsLoaded { get; private set; }

    public int CurrentInjuryRecords { get; private set; }

    public int HistoricalInjuryRecords { get; private set; }

    public DateTimeOffset? LastInjurySync { get; private set; }

    public TimeSpan? InjurySyncRuntime { get; private set; }

    public string? LastInjuryError { get; private set; }

    public bool UsedFallback { get; private set; }

    public bool UsedCache { get; private set; }

    public void SetConfigured(InjuryProviderKind kind)
    {
        lock (_gate)
        {
            ConfiguredProvider = kind.ToString();
        }
    }

    public void RecordSuccess(
        InjuryProviderKind active,
        int playersWithData,
        int recordsLoaded,
        int currentRecords,
        int historicalRecords,
        TimeSpan runtime,
        bool usedFallback,
        bool usedCache,
        string? priorError)
    {
        lock (_gate)
        {
            ActiveProvider = active.ToString();
            PlayersWithInjuryData = playersWithData;
            InjuryRecordsLoaded = recordsLoaded;
            CurrentInjuryRecords = currentRecords;
            HistoricalInjuryRecords = historicalRecords;
            InjurySyncRuntime = runtime;
            LastInjurySync = DateTimeOffset.Now;
            UsedFallback = usedFallback;
            UsedCache = usedCache;
            LastInjuryError = priorError;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            LastInjuryError = error;
        }
    }
}
