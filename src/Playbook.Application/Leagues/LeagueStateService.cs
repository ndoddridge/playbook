using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues;

/// <summary>
/// In-memory league selection state. Persists for the lifetime of the application process.
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

    public event Action? Changed;

    public IReadOnlyList<League> GetAllLeagues() => _leagueService.GetAllLeagues();

    public League? GetCurrentLeague() => _currentLeague;

    public void SelectLeague(Guid leagueId)
    {
        _leagueService.SelectLeague(leagueId);
        var selected = _leagueService.GetCurrentLeague();

        if (selected is null || selected.Id == _currentLeague?.Id)
        {
            _currentLeague = selected;
            return;
        }

        _currentLeague = selected;
        Changed?.Invoke();
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

    public async Task<LeagueConnectResult> ConnectSleeperLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default)
    {
        var result = await _leagueService
            .ConnectSleeperLeagueAsync(sleeperLeagueId, cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded && result.League is not null)
        {
            _currentLeague = result.League;
            Changed?.Invoke();
        }

        return result;
    }
}
