using Playbook.Application.Leagues;
using Playbook.Core.Leagues;

namespace Playbook.Infrastructure.Leagues;

/// <summary>
/// In-memory mock league catalog. Used as the demo fallback when no Sleeper league is connected.
/// </summary>
public sealed class MockLeagueService
{
    private readonly IReadOnlyList<League> _leagues;
    private readonly Dictionary<Guid, IReadOnlyList<FantasyTeam>> _teamsByLeague;
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
                IsActive = true,
                DataSource = LeagueDataSource.Mock,
                ReceptionPoints = 1.0m
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
                IsActive = true,
                DataSource = LeagueDataSource.Mock,
                ReceptionPoints = 0.5m
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
                IsActive = true,
                DataSource = LeagueDataSource.Mock,
                ReceptionPoints = 0m
            }
        ];

        _teamsByLeague = _leagues.ToDictionary(
            league => league.Id,
            league => (IReadOnlyList<FantasyTeam>)CreateDemoTeams(league));

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

    public IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId) =>
        _teamsByLeague.TryGetValue(leagueId, out var teams) ? teams : [];

    public FantasyTeam? FindTeamForPlayer(Guid leagueId, Guid playerId) =>
        GetTeams(leagueId).FirstOrDefault(team => team.PlayerIds.Contains(playerId));

    private static List<FantasyTeam> CreateDemoTeams(League league)
    {
        // Demo rosters stay empty of real player ids — My Teams shows an empty-state for mock leagues
        // so live Sleeper rosters remain clearly distinct.
        return Enumerable.Range(1, Math.Min(league.NumberOfTeams, 4))
            .Select(i => new FantasyTeam
            {
                LeagueId = league.Id,
                RosterId = i,
                DisplayName = $"Demo Owner {i}",
                TeamName = $"{league.Name} Team {i}",
                PlayerIds = [],
                StarterIds = [],
                ExternalPlayerIds = []
            })
            .ToList();
    }
}
