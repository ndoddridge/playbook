namespace Playbook.Application.Leagues;

/// <summary>
/// Persists the user's selected roster per league across process restarts.
/// Keys prefer platform external ids (Sleeper league id); mock leagues use a stable local key.
/// </summary>
public interface ILeagueUserTeamStore
{
    bool TryGetSelectedRosterId(string leagueKey, out int rosterId);

    void SaveSelectedRosterId(string leagueKey, int rosterId);

    static string KeyForExternalId(string externalLeagueId) =>
        $"sleeper:{externalLeagueId.Trim()}";

    static string KeyForLeagueId(Guid leagueId) =>
        $"league:{leagueId:N}";
}
