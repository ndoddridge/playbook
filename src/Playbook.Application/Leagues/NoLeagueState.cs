using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues;

/// <summary>
/// League state that never has a league. Used by shared (singleton) services that must not depend
/// on any one customer's league — e.g. Quick Picks and the background refresh.
/// </summary>
public sealed class NoLeagueState : ILeagueState
{
    public League? CurrentLeague => null;

    public FantasyTeam? CurrentUserTeam => null;

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<League> GetAllLeagues() => [];

    public League? GetCurrentLeague() => null;

    public void SelectLeague(Guid leagueId)
    {
    }

    public IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId) => [];

    public IReadOnlyList<FantasyTeam> GetCurrentTeams() => [];

    public FantasyTeam? FindTeamForPlayer(Guid playerId) => null;

    public FantasyTeam? GetUserTeam(Guid leagueId) => null;

    public FantasyTeam? GetCurrentUserTeam() => null;

    public bool SelectUserTeam(Guid leagueId, int rosterId) => false;

    public Task<LeagueConnectResult> ConnectSleeperLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(LeagueConnectResult.Fail("No league context."));
}
