using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues;

/// <summary>
/// Global application state for the currently selected fantasy league.
/// Engines and UI subscribe to <see cref="Changed"/> for live updates.
/// </summary>
public interface ILeagueState
{
    League? CurrentLeague { get; }

    FantasyTeam? CurrentUserTeam { get; }

    event Action? Changed;

    IReadOnlyList<League> GetAllLeagues();

    League? GetCurrentLeague();

    void SelectLeague(Guid leagueId);

    IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId);

    IReadOnlyList<FantasyTeam> GetCurrentTeams();

    FantasyTeam? FindTeamForPlayer(Guid playerId);

    FantasyTeam? GetUserTeam(Guid leagueId);

    FantasyTeam? GetCurrentUserTeam();

    bool SelectUserTeam(Guid leagueId, int rosterId);

    Task<LeagueConnectResult> ConnectSleeperLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default);
}
