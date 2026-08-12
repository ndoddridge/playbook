namespace Playbook.Application.Leagues;

/// <summary>
/// Persists the user's selected roster per league across process restarts.
/// Keys prefer platform external ids (Sleeper league id); mock leagues use a stable local key.
/// </summary>
public interface ILeagueUserTeamStore
{
    bool TryGetSelectedRosterId(string leagueKey, out int rosterId);

    void SaveSelectedRosterId(string leagueKey, int rosterId);

    /// <summary>
    /// Sleeper league ids the user has connected. Read once at startup to auto-reconnect
    /// without requiring the league id to be re-entered after every restart/redeploy.
    /// </summary>
    IReadOnlyList<string> GetConnectedExternalLeagueIds();

    /// <summary>Records a successful Sleeper connection so it survives process restarts.</summary>
    void SaveConnectedExternalLeagueId(string externalLeagueId);

    static string KeyForExternalId(string externalLeagueId) =>
        $"sleeper:{externalLeagueId.Trim()}";

    static string KeyForLeagueId(Guid leagueId) =>
        $"league:{leagueId:N}";
}
