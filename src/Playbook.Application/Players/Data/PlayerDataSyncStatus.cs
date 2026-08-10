namespace Playbook.Application.Players.Data;

/// <summary>
/// Mutable sync status updated by <c>PlayerService</c> after each load attempt.
/// </summary>
public sealed class PlayerDataSyncStatus : IPlayerDataSyncStatus
{
    private readonly object _gate = new();

    public string ConfiguredProvider { get; private set; } = PlayerDataProviderKind.Mock.ToString();

    public string ActiveProvider { get; private set; } = PlayerDataProviderKind.Mock.ToString();

    public DateTimeOffset? LastSuccessfulSync { get; private set; }

    public int PlayersLoaded { get; private set; }

    public TimeSpan? ProviderResponseTime { get; private set; }

    public string? LastError { get; private set; }

    public bool UsedFallback { get; private set; }

    public void SetConfigured(PlayerDataProviderKind kind)
    {
        lock (_gate)
        {
            ConfiguredProvider = kind.ToString();
        }
    }

    public void RecordSuccess(
        PlayerDataProviderKind active,
        int playersLoaded,
        TimeSpan responseTime,
        bool usedFallback,
        string? priorError)
    {
        lock (_gate)
        {
            ActiveProvider = active.ToString();
            PlayersLoaded = playersLoaded;
            ProviderResponseTime = responseTime;
            LastSuccessfulSync = DateTimeOffset.Now;
            UsedFallback = usedFallback;
            LastError = priorError;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            LastError = error;
        }
    }
}
