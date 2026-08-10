using Playbook.Application.Players;
using Playbook.Core.Leagues;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Leagues;

/// <summary>
/// In-memory mock league catalog. Used as the demo fallback when no Sleeper league is connected.
/// Demo leagues default to the first roster as the user's team so the app stays usable without Sleeper.
/// Rosters are resolved from the active player catalog by name so mock leagues work with live or mock IDs.
/// </summary>
public sealed class MockLeagueService
{
    private readonly IPlayerService _players;
    private readonly List<League> _leagues;
    private readonly Dictionary<Guid, IReadOnlyList<FantasyTeam>> _teamsByLeague = new();
    private readonly object _gate = new();
    private League? _currentLeague;
    private int _seededCatalogCount = -1;
    private Guid? _seededCatalogProbeId;

    public MockLeagueService(IPlayerService players)
    {
        _players = players;
        _leagues =
        [
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

    public IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId)
    {
        EnsureRostersSeeded();
        return _teamsByLeague.TryGetValue(leagueId, out var teams) ? teams : [];
    }

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

    private void EnsureRostersSeeded()
    {
        var catalog = _players.GetAllPlayers();
        var probeId = catalog.FirstOrDefault()?.Id;
        if (_seededCatalogCount == catalog.Count &&
            _seededCatalogProbeId == probeId &&
            _teamsByLeague.Count == _leagues.Count)
        {
            return;
        }

        lock (_gate)
        {
            catalog = _players.GetAllPlayers();
            probeId = catalog.FirstOrDefault()?.Id;
            if (_seededCatalogCount == catalog.Count &&
                _seededCatalogProbeId == probeId &&
                _teamsByLeague.Count == _leagues.Count)
            {
                return;
            }

            foreach (var league in _leagues)
            {
                _teamsByLeague[league.Id] = CreateDemoTeams(league, catalog);
            }

            _seededCatalogCount = catalog.Count;
            _seededCatalogProbeId = probeId;
        }
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

    /// <summary>
    /// Preferred demo roster identities — resolved against whatever catalog is active (live or mock).
    /// </summary>
    private static readonly string[] DemoPlayerNames =
    [
        "Jayden Daniels",
        "Jordan Love",
        "Patrick Mahomes",
        "Bucky Irving",
        "Bijan Robinson",
        "Saquon Barkley",
        "Jahmyr Gibbs",
        "Brian Thomas",
        "Ja'Marr Chase",
        "CeeDee Lamb",
        "Amon-Ra St. Brown",
        "Puka Nacua",
        "Travis Kelce",
        "Brock Bowers",
        "Trey McBride",
        "Justin Tucker",
        "Harrison Butker",
        "Buffalo Bills",
        "San Francisco 49ers",
        "Philadelphia Eagles"
    ];

    private static List<FantasyTeam> CreateDemoTeams(League league, IReadOnlyList<Player> catalog)
    {
        var resolvedPool = ResolveDemoPool(catalog);
        var leagueOffset = Math.Abs(league.Id.GetHashCode()) % Math.Max(1, resolvedPool.Count);

        return Enumerable.Range(1, Math.Min(league.NumberOfTeams, 4))
            .Select(i =>
            {
                var offset = (leagueOffset + (i - 1) * 4) % Math.Max(1, resolvedPool.Count);
                var playerIds = resolvedPool.Count == 0
                    ? new List<Guid>()
                    : Enumerable.Range(0, Math.Min(8, resolvedPool.Count))
                        .Select(k => resolvedPool[(offset + k) % resolvedPool.Count])
                        .Distinct()
                        .ToList();
                var starters = playerIds.Take(Math.Min(6, playerIds.Count)).ToList();
                return new FantasyTeam
                {
                    LeagueId = league.Id,
                    RosterId = i,
                    DisplayName = $"Demo Owner {i}",
                    TeamName = $"{league.Name} Team {i}",
                    PlayerIds = playerIds,
                    StarterIds = starters,
                    ExternalPlayerIds = []
                };
            })
            .ToList();
    }

    private static IReadOnlyList<Guid> ResolveDemoPool(IReadOnlyList<Player> catalog)
    {
        if (catalog.Count == 0)
        {
            return [];
        }

        var byName = new List<Guid>();
        foreach (var name in DemoPlayerNames)
        {
            var match = catalog.FirstOrDefault(p =>
                string.Equals(p.FullName, name, StringComparison.OrdinalIgnoreCase) ||
                p.FullName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(p.FullName, StringComparison.OrdinalIgnoreCase));
            if (match is not null && byName.All(id => id != match.Id))
            {
                byName.Add(match.Id);
            }
        }

        if (byName.Count >= 8)
        {
            return byName;
        }

        // Position-balanced fallback when live catalog naming differs.
        var fallback = new List<Guid>(byName);
        foreach (var position in new[] { Position.QB, Position.RB, Position.WR, Position.TE, Position.K, Position.DST })
        {
            foreach (var player in catalog.Where(p => p.Position == position).Take(4))
            {
                if (fallback.All(id => id != player.Id))
                {
                    fallback.Add(player.Id);
                }
            }
        }

        foreach (var player in catalog)
        {
            if (fallback.Count >= 24)
            {
                break;
            }

            if (fallback.All(id => id != player.Id))
            {
                fallback.Add(player.Id);
            }
        }

        return fallback;
    }
}
