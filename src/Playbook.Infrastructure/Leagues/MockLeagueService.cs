using Playbook.Application.Leagues;
using Playbook.Core.Leagues;

namespace Playbook.Infrastructure.Leagues;

/// <summary>
/// In-memory mock league catalog. Used as the demo fallback when no Sleeper league is connected.
/// Demo leagues default to the first roster as the user's team so the app stays usable without Sleeper.
/// </summary>
public sealed class MockLeagueService
{
    private readonly List<League> _leagues;
    private readonly Dictionary<Guid, IReadOnlyList<FantasyTeam>> _teamsByLeague;
    private League? _currentLeague;

    public MockLeagueService()
    {
        var seeds = new List<League>
        {
            new()
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
                ReceptionPoints = 1.0m,
                SelectedRosterId = 1
            },
            new()
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
                ReceptionPoints = 0.5m,
                SelectedRosterId = 1
            },
            new()
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
                ReceptionPoints = 0m,
                SelectedRosterId = 1
            }
        };

        _leagues = seeds;
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

    public FantasyTeam? GetUserTeam(Guid leagueId)
    {
        var league = _leagues.FirstOrDefault(l => l.Id == leagueId);
        if (league?.SelectedRosterId is not int rosterId)
        {
            return null;
        }

        return GetTeams(leagueId).FirstOrDefault(t => t.RosterId == rosterId);
    }

    public bool SelectUserTeam(Guid leagueId, int rosterId)
    {
        var index = _leagues.FindIndex(l => l.Id == leagueId);
        if (index < 0)
        {
            return false;
        }

        var team = GetTeams(leagueId).FirstOrDefault(t => t.RosterId == rosterId);
        if (team is null)
        {
            return false;
        }

        var existing = _leagues[index];
        var updated = CloneWithSelectedRoster(existing, rosterId);
        _leagues[index] = updated;
        if (_currentLeague?.Id == leagueId)
        {
            _currentLeague = updated;
        }

        return true;
    }

    private static League CloneWithSelectedRoster(League league, int rosterId) =>
        new()
        {
            Id = league.Id,
            Name = league.Name,
            Platform = league.Platform,
            LeagueType = league.LeagueType,
            ScoringType = league.ScoringType,
            NumberOfTeams = league.NumberOfTeams,
            CurrentWeek = league.CurrentWeek,
            Season = league.Season,
            IsActive = league.IsActive,
            ExternalId = league.ExternalId,
            DataSource = league.DataSource,
            ReceptionPoints = league.ReceptionPoints,
            SelectedRosterId = rosterId
        };

    private static List<FantasyTeam> CreateDemoTeams(League league)
    {
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
