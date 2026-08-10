using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues;

/// <summary>
/// In-memory league selection state. Persists for the lifetime of the application process.
/// User-team choices for live leagues are persisted by the league service store.
/// </summary>
public sealed class LeagueStateService : ILeagueState
{
    private readonly ILeagueService _leagueService;
    private League? _currentLeague;

    public LeagueStateService(ILeagueService leagueService)
    {
        _leagueService = leagueService;
        _currentLeague = leagueService.GetCurrentLeague();
    }

    public League? CurrentLeague => _currentLeague;

    public FantasyTeam? CurrentUserTeam =>
        _currentLeague is null ? null : _leagueService.GetUserTeam(_currentLeague.Id);

    public event Action? Changed;

    public IReadOnlyList<League> GetAllLeagues() => _leagueService.GetAllLeagues();

    public League? GetCurrentLeague() => _currentLeague;

    public void SelectLeague(Guid leagueId)
    {
        _leagueService.SelectLeague(leagueId);
        var selected = _leagueService.GetCurrentLeague();
        var previousId = _currentLeague?.Id;
        var previousRoster = _currentLeague?.SelectedRosterId;

        _currentLeague = selected;
        if (selected?.Id != previousId || selected?.SelectedRosterId != previousRoster)
        {
            Changed?.Invoke();
        }
    }

    public IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId) =>
        _leagueService.GetTeams(leagueId);

    public IReadOnlyList<FantasyTeam> GetCurrentTeams() =>
        _currentLeague is null
            ? []
            : _leagueService.GetTeams(_currentLeague.Id);

    public FantasyTeam? FindTeamForPlayer(Guid playerId) =>
        _currentLeague is null
            ? null
            : _leagueService.FindTeamForPlayer(_currentLeague.Id, playerId);

    public FantasyTeam? GetUserTeam(Guid leagueId) =>
        _leagueService.GetUserTeam(leagueId);

    public FantasyTeam? GetCurrentUserTeam() => CurrentUserTeam;

    public bool SelectUserTeam(Guid leagueId, int rosterId)
    {
        var ok = _leagueService.SelectUserTeam(leagueId, rosterId);
        if (!ok)
        {
            return false;
        }

        _currentLeague = _leagueService.GetCurrentLeague();
        Changed?.Invoke();
        return true;
    }

    public async Task<LeagueConnectResult> ConnectSleeperLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default)
    {
        var result = await _leagueService
            .ConnectSleeperLeagueAsync(sleeperLeagueId, cancellationToken)
            .ConfigureAwait(false);

        // Only adopt as current when setup is complete (saved/restored team).
        // Incomplete connects keep the previous current league until the user picks a team.
        if (result.IsSetupComplete && result.League is not null)
        {
            _currentLeague = result.League;
            Changed?.Invoke();
        }
        else if (result.Succeeded)
        {
            // Catalog gained a pending league — refresh subscribers without forcing current.
            Changed?.Invoke();
        }

        return result;
    }
}
