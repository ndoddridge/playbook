using Playbook.Application.Leagues;
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
}
