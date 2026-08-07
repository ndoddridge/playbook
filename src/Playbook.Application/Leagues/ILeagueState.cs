using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues;

/// <summary>
/// Global application state for the currently selected fantasy league.
/// Engines and UI subscribe to <see cref="Changed"/> for live updates.
/// </summary>
public interface ILeagueState
{
    League? CurrentLeague { get; }

    event Action? Changed;

    IReadOnlyList<League> GetAllLeagues();

    League? GetCurrentLeague();

    void SelectLeague(Guid leagueId);
}
