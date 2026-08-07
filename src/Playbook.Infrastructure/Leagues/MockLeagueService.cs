using Playbook.Application.Leagues;
using Playbook.Core.Leagues;

namespace Playbook.Infrastructure.Leagues;

/// <summary>
/// In-memory mock league catalog and selection. Replace with API-backed persistence later.
/// </summary>
public sealed class MockLeagueService : ILeagueService
{
    private readonly IReadOnlyList<League> _leagues;
    private League? _currentLeague;

    public MockLeagueService()
    {
        _leagues =
        [
            new League
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Friends League",
                Platform = LeaguePlatform.Sleeper,
                LeagueType = LeagueType.Redraft,
                ScoringType = ScoringType.Ppr,
                NumberOfTeams = 12,
                CurrentWeek = 1,
                Season = 2026,
                IsActive = true
            },
            new League
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Dynasty League",
                Platform = LeaguePlatform.Sleeper,
                LeagueType = LeagueType.Dynasty,
                ScoringType = ScoringType.HalfPpr,
                NumberOfTeams = 12,
                CurrentWeek = 1,
                Season = 2026,
                IsActive = true
            },
            new League
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Work League",
                Platform = LeaguePlatform.ESPN,
                LeagueType = LeagueType.Redraft,
                ScoringType = ScoringType.Standard,
                NumberOfTeams = 10,
                CurrentWeek = 1,
                Season = 2026,
                IsActive = true
            }
        ];

        _currentLeague = _leagues[0];
    }

    public IReadOnlyList<League> GetAllLeagues() => _leagues;

    public League? GetCurrentLeague() => _currentLeague;

    public void SelectLeague(Guid leagueId)
    {
        var match = _leagues.FirstOrDefault(league => league.Id == leagueId);
        if (match is not null)
        {
            _currentLeague = match;
        }
    }
}
