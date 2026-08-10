using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues;

public interface ILeagueService
{
    IReadOnlyList<League> GetAllLeagues();

    League? GetCurrentLeague();

    void SelectLeague(Guid leagueId);

    IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId);

    FantasyTeam? FindTeamForPlayer(Guid leagueId, Guid playerId);

    FantasyTeam? GetUserTeam(Guid leagueId);

    FantasyTeam? GetCurrentUserTeam();

    /// <summary>
    /// Sets the user's own roster for a league, persists the choice for live leagues,
    /// and marks setup complete. Returns false if the roster is not in that league.
    /// </summary>
    bool SelectUserTeam(Guid leagueId, int rosterId);

    Task<LeagueConnectResult> ConnectSleeperLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default);
}
