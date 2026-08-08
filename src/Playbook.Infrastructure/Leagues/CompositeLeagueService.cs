using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Playbook.Application.Leagues;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Core.Leagues;
using Playbook.Infrastructure.Players;

namespace Playbook.Infrastructure.Leagues;

/// <summary>
/// Combines mock demo leagues with on-demand Sleeper league connections.
/// Mock catalog remains available when no live league is connected.
/// </summary>
public sealed class CompositeLeagueService : ILeagueService
{
    private readonly MockLeagueService _mock;
    private readonly ISleeperLeagueClient _sleeperClient;
    private readonly LeagueSyncStatus _syncStatus;
    private readonly ILogger<CompositeLeagueService> _logger;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, Guid> _externalIdToLeagueId =
        new(StringComparer.Ordinal);

    private readonly List<League> _liveLeagues = [];
    private readonly Dictionary<Guid, IReadOnlyList<FantasyTeam>> _liveTeams = new();
    private League? _currentLeague;

    public CompositeLeagueService(
        MockLeagueService mock,
        ISleeperLeagueClient sleeperClient,
        LeagueSyncStatus syncStatus,
        ILogger<CompositeLeagueService> logger)
    {
        _mock = mock;
        _sleeperClient = sleeperClient;
        _syncStatus = syncStatus;
        _logger = logger;
        _currentLeague = mock.GetCurrentLeague();
    }

    public IReadOnlyList<League> GetAllLeagues()
    {
        lock (_gate)
        {
            return _mock.GetAllLeagues().Concat(_liveLeagues).ToList();
        }
    }

    public League? GetCurrentLeague()
    {
        lock (_gate)
        {
            return _currentLeague;
        }
    }

    public void SelectLeague(Guid leagueId)
    {
        lock (_gate)
        {
            var live = _liveLeagues.FirstOrDefault(l => l.Id == leagueId);
            if (live is not null)
            {
                _currentLeague = live;
                return;
            }

            _mock.SelectLeague(leagueId);
            var mockSelected = _mock.GetCurrentLeague();
            if (mockSelected is not null && mockSelected.Id == leagueId)
            {
                _currentLeague = mockSelected;
            }
        }
    }

    public IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId)
    {
        lock (_gate)
        {
            if (_liveTeams.TryGetValue(leagueId, out var liveTeams))
            {
                return liveTeams;
            }
        }

        return _mock.GetTeams(leagueId);
    }

    public FantasyTeam? FindTeamForPlayer(Guid leagueId, Guid playerId) =>
        GetTeams(leagueId).FirstOrDefault(team => team.PlayerIds.Contains(playerId));

    public async Task<LeagueConnectResult> ConnectSleeperLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sleeperLeagueId))
        {
            _syncStatus.RecordFailure("Enter a Sleeper league ID.");
            return LeagueConnectResult.Fail("Enter a Sleeper league ID.");
        }

        var normalizedId = sleeperLeagueId.Trim();
        _syncStatus.SetConnecting(true);

        try
        {
            var snapshot = await _sleeperClient
                .GetLeagueSnapshotAsync(normalizedId, cancellationToken)
                .ConfigureAwait(false);

            if (snapshot is null)
            {
                var missing = $"No Sleeper league found for ID '{normalizedId}'.";
                _syncStatus.RecordFailure(missing);
                return LeagueConnectResult.Fail(missing);
            }

            var (scoring, receptionPoints) =
                SleeperScoringMapper.MapReceptionScoring(snapshot.ScoringSettings);
            var leagueType = SleeperScoringMapper.MapLeagueType(snapshot.SleeperLeagueType);
            var season = int.TryParse(snapshot.Season, out var seasonYear)
                ? seasonYear
                : DateTime.UtcNow.Year;

            Guid leagueId;
            lock (_gate)
            {
                if (!_externalIdToLeagueId.TryGetValue(snapshot.ExternalLeagueId, out leagueId))
                {
                    leagueId = Guid.NewGuid();
                    _externalIdToLeagueId[snapshot.ExternalLeagueId] = leagueId;
                }
            }

            var league = new League
            {
                Id = leagueId,
                Name = snapshot.Name,
                Platform = LeaguePlatform.Sleeper,
                LeagueType = leagueType,
                ScoringType = scoring,
                NumberOfTeams = snapshot.TeamCount,
                CurrentWeek = Math.Max(1, snapshot.CurrentWeek),
                Season = season,
                IsActive = !string.Equals(snapshot.Status, "complete", StringComparison.OrdinalIgnoreCase),
                ExternalId = snapshot.ExternalLeagueId,
                DataSource = LeagueDataSource.Sleeper,
                ReceptionPoints = receptionPoints
            };

            var teams = snapshot.Rosters
                .Select(roster => MapRoster(leagueId, roster))
                .ToList();

            int liveCount;
            lock (_gate)
            {
                var existingIndex = _liveLeagues.FindIndex(l => l.Id == leagueId);
                if (existingIndex >= 0)
                {
                    _liveLeagues[existingIndex] = league;
                }
                else
                {
                    _liveLeagues.Add(league);
                }

                _liveTeams[leagueId] = teams;
                _currentLeague = league;
                liveCount = _liveLeagues.Count;
            }

            _syncStatus.RecordSuccess(
                snapshot.ExternalLeagueId,
                snapshot.Name,
                liveCount,
                teams.Count);

            _logger.LogInformation(
                "Connected Sleeper league {LeagueName} ({ExternalId}) with {TeamCount} teams, scoring {Scoring}.",
                league.Name,
                league.ExternalId,
                teams.Count,
                SleeperScoringMapper.FormatLabel(scoring, receptionPoints));

            return LeagueConnectResult.Ok(league, teams);
        }
        catch (OperationCanceledException)
        {
            _syncStatus.RecordFailure("Sleeper league connection was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect Sleeper league {LeagueId}", normalizedId);
            var message = "Could not reach Sleeper or parse the league response. Try again shortly.";
            _syncStatus.RecordFailure(message);
            return LeagueConnectResult.Fail(message);
        }
    }

    private static FantasyTeam MapRoster(Guid leagueId, SleeperRosterSnapshot roster)
    {
        var playbookIds = roster.SleeperPlayerIds
            .Where(id => !string.IsNullOrWhiteSpace(id) &&
                         !string.Equals(id, "0", StringComparison.Ordinal))
            .Select(SleeperPlayerIds.ToPlaybookId)
            .Distinct()
            .ToList();

        var starterIds = roster.StarterSleeperPlayerIds
            .Where(id => !string.IsNullOrWhiteSpace(id) &&
                         !string.Equals(id, "0", StringComparison.Ordinal))
            .Select(SleeperPlayerIds.ToPlaybookId)
            .Distinct()
            .ToList();

        return new FantasyTeam
        {
            LeagueId = leagueId,
            RosterId = roster.RosterId,
            OwnerUserId = roster.OwnerId,
            DisplayName = roster.OwnerName,
            TeamName = roster.TeamName,
            PlayerIds = playbookIds,
            StarterIds = starterIds,
            ExternalPlayerIds = roster.SleeperPlayerIds
        };
    }
}
