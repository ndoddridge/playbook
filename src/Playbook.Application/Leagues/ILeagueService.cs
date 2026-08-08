using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues;

public interface ILeagueService
{
    IReadOnlyList<League> GetAllLeagues();

    League? GetCurrentLeague();

    void SelectLeague(Guid leagueId);

    IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId);

    FantasyTeam? FindTeamForPlayer(Guid leagueId, Guid playerId);

    Task<LeagueConnectResult> ConnectSleeperLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default);
}
