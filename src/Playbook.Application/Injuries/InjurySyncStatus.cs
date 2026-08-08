using Playbook.Core.Injuries.Models;

namespace Playbook.Application.Injuries;

public interface IInjurySyncStatus
{
    string ConfiguredProvider { get; }

    string ActiveProvider { get; }

    int PlayersWithInjuryData { get; }

    int PlayersWithCurrentInjuries { get; }

    int PlayersWithHistoricalData { get; }

    int InjuryRecordsLoaded { get; }

    int CurrentInjuryRecords { get; }

    int HistoricalInjuryRecords { get; }

    string HistoricalDataAvailability { get; }

    DateTimeOffset? LastInjurySync { get; }

    TimeSpan? InjurySyncRuntime { get; }

    string? LastInjuryError { get; }

    bool UsedFallback { get; }

    bool UsedCache { get; }

    bool SupportsHistoricalInjuries { get; }
}

public sealed class InjurySyncStatus : IInjurySyncStatus
{
    private readonly object _gate = new();

    public string ConfiguredProvider { get; private set; } = InjuryProviderKind.Mock.ToString();

    public string ActiveProvider { get; private set; } = InjuryProviderKind.Mock.ToString();

    public int PlayersWithInjuryData { get; private set; }

    public int PlayersWithCurrentInjuries { get; private set; }

    public int PlayersWithHistoricalData { get; private set; }

    public int InjuryRecordsLoaded { get; private set; }

    public int CurrentInjuryRecords { get; private set; }

    public int HistoricalInjuryRecords { get; private set; }

    public string HistoricalDataAvailability { get; private set; } =
        HistoricalDataStatus.NotSynced.ToString();

    public DateTimeOffset? LastInjurySync { get; private set; }

    public TimeSpan? InjurySyncRuntime { get; private set; }

    public string? LastInjuryError { get; private set; }

    public bool UsedFallback { get; private set; }

    public bool UsedCache { get; private set; }

    public bool SupportsHistoricalInjuries { get; private set; }

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
        int playersWithCurrent,
        int playersWithHistorical,
        int recordsLoaded,
        int currentRecords,
        int historicalRecords,
        HistoricalDataStatus historicalAvailability,
        bool supportsHistorical,
        TimeSpan runtime,
        bool usedFallback,
        bool usedCache,
        string? priorError)
    {
        lock (_gate)
        {
            ActiveProvider = active.ToString();
            PlayersWithInjuryData = playersWithData;
            PlayersWithCurrentInjuries = playersWithCurrent;
            PlayersWithHistoricalData = playersWithHistorical;
            InjuryRecordsLoaded = recordsLoaded;
            CurrentInjuryRecords = currentRecords;
            HistoricalInjuryRecords = historicalRecords;
            HistoricalDataAvailability = historicalAvailability.ToString();
            SupportsHistoricalInjuries = supportsHistorical;
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
