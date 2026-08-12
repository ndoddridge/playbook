using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Leagues;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Core.Leagues;
using Playbook.Infrastructure.Players;

namespace Playbook.Infrastructure.Leagues;

/// <summary>
/// Combines on-demand Sleeper league connections with the fixed demo leagues (only when
/// <see cref="LeagueOptions.EnableMockLeagues"/> is on — off in the deployed personal-use
/// product, where connected leagues are the only source of truth and no mock team is ever
/// auto-created). Live league setup is incomplete until the user selects their own roster.
/// </summary>
public sealed class CompositeLeagueService : ILeagueService
{
    private readonly MockLeagueService _mock;
    private readonly ISleeperLeagueClient _sleeperClient;
    private readonly ILeagueUserTeamStore _userTeamStore;
    private readonly LeagueSyncStatus _syncStatus;
    private readonly ILogger<CompositeLeagueService> _logger;
    private readonly bool _mockEnabled;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, Guid> _externalIdToLeagueId =
        new(StringComparer.Ordinal);

    private readonly List<League> _liveLeagues = [];
    private readonly Dictionary<Guid, IReadOnlyList<FantasyTeam>> _liveTeams = new();
    private League? _currentLeague;

    public CompositeLeagueService(
        MockLeagueService mock,
        ISleeperLeagueClient sleeperClient,
        ILeagueUserTeamStore userTeamStore,
        LeagueSyncStatus syncStatus,
        IOptions<LeagueOptions> options,
        ILogger<CompositeLeagueService> logger)
    {
        _mock = mock;
        _sleeperClient = sleeperClient;
        _userTeamStore = userTeamStore;
        _syncStatus = syncStatus;
        _logger = logger;
        _mockEnabled = options.Value.EnableMockLeagues;
        _currentLeague = _mockEnabled ? mock.GetCurrentLeague() : null;
    }

    public IReadOnlyList<League> GetAllLeagues()
    {
        lock (_gate)
        {
            return _mockEnabled ? _mock.GetAllLeagues().Concat(_liveLeagues).ToList() : _liveLeagues.ToList();
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

            if (!_mockEnabled)
            {
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

    public FantasyTeam? GetUserTeam(Guid leagueId)
    {
        League? league;
        lock (_gate)
        {
            league = _liveLeagues.FirstOrDefault(l => l.Id == leagueId)
                     ?? _mock.GetAllLeagues().FirstOrDefault(l => l.Id == leagueId);
        }

        if (league?.SelectedRosterId is not int rosterId)
        {
            return null;
        }

        return GetTeams(leagueId).FirstOrDefault(t => t.RosterId == rosterId);
    }

    public FantasyTeam? GetCurrentUserTeam()
    {
        var current = GetCurrentLeague();
        return current is null ? null : GetUserTeam(current.Id);
    }

    public bool SelectUserTeam(Guid leagueId, int rosterId)
    {
        lock (_gate)
        {
            var liveIndex = _liveLeagues.FindIndex(l => l.Id == leagueId);
            if (liveIndex >= 0)
            {
                if (!_liveTeams.TryGetValue(leagueId, out var teams) ||
                    teams.All(t => t.RosterId != rosterId))
                {
                    return false;
                }

                var existing = _liveLeagues[liveIndex];
                var updated = CloneWithSelectedRoster(existing, rosterId);
                _liveLeagues[liveIndex] = updated;
                _currentLeague = updated;

                if (!string.IsNullOrWhiteSpace(updated.ExternalId))
                {
                    _userTeamStore.SaveSelectedRosterId(
                        ILeagueUserTeamStore.KeyForExternalId(updated.ExternalId),
                        rosterId);
                }
                else
                {
                    _userTeamStore.SaveSelectedRosterId(
                        ILeagueUserTeamStore.KeyForLeagueId(updated.Id),
                        rosterId);
                }

                _logger.LogInformation(
                    "Selected user team roster {RosterId} for league {LeagueName} ({ExternalId}).",
                    rosterId,
                    updated.Name,
                    updated.ExternalId ?? updated.Id.ToString());
                return true;
            }
        }

        if (!_mock.SelectUserTeam(leagueId, rosterId))
        {
            return false;
        }

        lock (_gate)
        {
            _currentLeague = _mock.GetCurrentLeague();
        }

        _userTeamStore.SaveSelectedRosterId(
            ILeagueUserTeamStore.KeyForLeagueId(leagueId),
            rosterId);
        return true;
    }

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

            var storeKey = ILeagueUserTeamStore.KeyForExternalId(snapshot.ExternalLeagueId);
            int? restoredRosterId = null;
            if (_userTeamStore.TryGetSelectedRosterId(storeKey, out var savedRosterId) &&
                snapshot.Rosters.Any(r => r.RosterId == savedRosterId))
            {
                restoredRosterId = savedRosterId;
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
                ReceptionPoints = receptionPoints,
                SelectedRosterId = restoredRosterId,
                RosterPositions = snapshot.RosterPositions
            };

            var teams = snapshot.Rosters
                .Select(roster => MapRoster(leagueId, roster))
                .ToList();

            if (teams.Count == 0)
            {
                _syncStatus.RecordFailure("This Sleeper league has no teams/rosters yet.");
                return LeagueConnectResult.Fail("This Sleeper league has no teams/rosters yet.");
            }

            // Remember this connection so it auto-reconnects on the next process start/redeploy
            // instead of requiring the league id to be re-entered.
            _userTeamStore.SaveConnectedExternalLeagueId(snapshot.ExternalLeagueId);

            var needsTeamSelection = restoredRosterId is null;
            FantasyTeam? selectedTeam = restoredRosterId is int rid
                ? teams.FirstOrDefault(t => t.RosterId == rid)
                : null;

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

                // Only become current when the user's team is known.
                if (!needsTeamSelection)
                {
                    _currentLeague = league;
                }

                liveCount = _liveLeagues.Count;
            }

            _syncStatus.RecordSuccess(
                snapshot.ExternalLeagueId,
                snapshot.Name,
                liveCount,
                teams.Count);

            _logger.LogInformation(
                "Connected Sleeper league {LeagueName} ({ExternalId}) with {TeamCount} teams, scoring {Scoring}. Setup complete: {Complete}.",
                league.Name,
                league.ExternalId,
                teams.Count,
                SleeperScoringMapper.FormatLabel(scoring, receptionPoints),
                !needsTeamSelection);

            return LeagueConnectResult.Ok(league, teams, selectedTeam, needsTeamSelection);
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
            SelectedRosterId = rosterId,
            RosterPositions = league.RosterPositions
        };

    private static FantasyTeam MapRoster(Guid leagueId, SleeperRosterSnapshot roster)
    {
        var playbookIds = ToPlaybookIds(roster.SleeperPlayerIds);
        var starterIds = ToPlaybookIds(roster.StarterSleeperPlayerIds);
        var taxiIds = ToPlaybookIds(roster.TaxiSleeperPlayerIds);
        var reserveIds = ToPlaybookIds(roster.ReserveSleeperPlayerIds);

        return new FantasyTeam
        {
            LeagueId = leagueId,
            RosterId = roster.RosterId,
            OwnerUserId = roster.OwnerId,
            DisplayName = roster.OwnerName,
            TeamName = roster.TeamName,
            PlayerIds = playbookIds,
            StarterIds = starterIds,
            TaxiPlayerIds = taxiIds,
            ReservePlayerIds = reserveIds,
            ExternalPlayerIds = roster.SleeperPlayerIds
        };
    }

    private static List<Guid> ToPlaybookIds(IReadOnlyList<string> sleeperIds) =>
        sleeperIds
            .Where(id => !string.IsNullOrWhiteSpace(id) &&
                         !string.Equals(id, "0", StringComparison.Ordinal))
            .Select(SleeperPlayerIds.ToPlaybookId)
            .Distinct()
            .ToList();
}
