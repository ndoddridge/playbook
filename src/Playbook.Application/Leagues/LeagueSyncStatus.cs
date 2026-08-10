namespace Playbook.Application.Leagues;

public interface ILeagueSyncStatus
{
    string CatalogMode { get; }

    string? LastConnectedExternalId { get; }

    string? LastConnectedLeagueName { get; }

    int LiveLeaguesLoaded { get; }

    int TeamsLoaded { get; }

    DateTimeOffset? LastConnectTime { get; }

    string? LastError { get; }

    bool IsConnecting { get; }
}

public sealed class LeagueSyncStatus : ILeagueSyncStatus
{
    private readonly object _gate = new();

    public string CatalogMode { get; private set; } = "Mock + Sleeper connect";

    public string? LastConnectedExternalId { get; private set; }

    public string? LastConnectedLeagueName { get; private set; }

    public int LiveLeaguesLoaded { get; private set; }

    public int TeamsLoaded { get; private set; }

    public DateTimeOffset? LastConnectTime { get; private set; }

    public string? LastError { get; private set; }

    public bool IsConnecting { get; private set; }

    public void SetConnecting(bool connecting)
    {
        lock (_gate)
        {
            IsConnecting = connecting;
        }
    }

    public void RecordSuccess(string externalId, string leagueName, int liveLeagues, int teams)
    {
        lock (_gate)
        {
            IsConnecting = false;
            LastConnectedExternalId = externalId;
            LastConnectedLeagueName = leagueName;
            LiveLeaguesLoaded = liveLeagues;
            TeamsLoaded = teams;
            LastConnectTime = DateTimeOffset.Now;
            LastError = null;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            IsConnecting = false;
            LastError = error;
            LastConnectTime = DateTimeOffset.Now;
        }
    }
}
