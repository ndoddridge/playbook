using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues;

public interface ILeagueService
{
    IReadOnlyList<League> GetAllLeagues();

    League? GetCurrentLeague();

    void SelectLeague(Guid leagueId);
}
