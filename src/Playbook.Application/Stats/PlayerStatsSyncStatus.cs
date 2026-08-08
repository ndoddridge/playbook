using Playbook.Application.Stats.Interfaces;

namespace Playbook.Application.Stats;

public interface IPlayerStatsSyncStatus
{
    string ConfiguredProvider { get; }

    string ActiveProvider { get; }

    string StatisticsProviders { get; }

    int PlayersWithStats { get; }

    int NflPlayersLoaded { get; }

    int SeasonsLoaded { get; }

    int NflSeasonsLoaded { get; }

    int CurrentSeasonRecords { get; }

    int HistoricalRecords { get; }

    int CollegeRecords { get; }

    int GameLogsLoaded { get; }

    int CollegeRecordsLoaded { get; }

    int IdentityMatches { get; }

    int UnresolvedPlayers { get; }

    DateTimeOffset? LastStatsSync { get; }

    DateTimeOffset? LastSuccessfulUpdate { get; }

    TimeSpan? StatsSyncRuntime { get; }

    string? LastStatsError { get; }

    string? ProviderErrors { get; }

    bool UsedFallback { get; }

    bool UsedCache { get; }
}

public sealed class PlayerStatsSyncStatus : IPlayerStatsSyncStatus
{
    private readonly object _gate = new();

    public string ConfiguredProvider { get; private set; } = PlayerStatsProviderKind.Mock.ToString();

    public string ActiveProvider { get; private set; } = PlayerStatsProviderKind.Mock.ToString();

    public string StatisticsProviders { get; private set; } = "—";

    public int PlayersWithStats { get; private set; }

    public int NflPlayersLoaded { get; private set; }

    public int SeasonsLoaded { get; private set; }

    public int NflSeasonsLoaded { get; private set; }

    public int CurrentSeasonRecords { get; private set; }

    public int HistoricalRecords { get; private set; }

    public int CollegeRecords { get; private set; }

    public int GameLogsLoaded { get; private set; }

    public int CollegeRecordsLoaded { get; private set; }

    public int IdentityMatches { get; private set; }

    public int UnresolvedPlayers { get; private set; }

    public DateTimeOffset? LastStatsSync { get; private set; }

    public DateTimeOffset? LastSuccessfulUpdate { get; private set; }

    public TimeSpan? StatsSyncRuntime { get; private set; }

    public string? LastStatsError { get; private set; }

    public string? ProviderErrors { get; private set; }

    public bool UsedFallback { get; private set; }

    public bool UsedCache { get; private set; }

    public void SetConfigured(PlayerStatsProviderKind kind, HistoricalPlayerStatsProviderKind historical)
    {
        lock (_gate)
        {
            ConfiguredProvider = kind.ToString();
            StatisticsProviders = $"{kind} + {historical}";
        }
    }

    public void RecordSuccess(
        PlayerStatsProviderKind active,
        string statisticsProviders,
        int playersWithStats,
        int nflPlayersLoaded,
        int seasonsLoaded,
        int nflSeasonsLoaded,
        int currentSeasonRecords,
        int historicalRecords,
        int collegeRecords,
        int gameLogsLoaded,
        int identityMatches,
        int unresolvedPlayers,
        TimeSpan runtime,
        bool usedFallback,
        bool usedCache,
        string? priorError)
    {
        lock (_gate)
        {
            ActiveProvider = active.ToString();
            StatisticsProviders = statisticsProviders;
            PlayersWithStats = playersWithStats;
            NflPlayersLoaded = nflPlayersLoaded;
            SeasonsLoaded = seasonsLoaded;
            NflSeasonsLoaded = nflSeasonsLoaded;
            CurrentSeasonRecords = currentSeasonRecords;
            HistoricalRecords = historicalRecords;
            CollegeRecords = collegeRecords;
            CollegeRecordsLoaded = collegeRecords;
            GameLogsLoaded = gameLogsLoaded;
            IdentityMatches = identityMatches;
            UnresolvedPlayers = unresolvedPlayers;
            StatsSyncRuntime = runtime;
            LastStatsSync = DateTimeOffset.Now;
            LastSuccessfulUpdate = DateTimeOffset.Now;
            UsedFallback = usedFallback;
            UsedCache = usedCache;
            LastStatsError = priorError;
            ProviderErrors = priorError;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            LastStatsError = error;
            ProviderErrors = error;
            LastStatsSync = DateTimeOffset.Now;
        }
    }
}
