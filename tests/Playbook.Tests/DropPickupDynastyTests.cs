using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Infrastructure.Intelligence.Services;

namespace Playbook.Tests;

/// <summary>
/// Covers the ImmediateValue/DynastyValue split: DynastyValue only applies for Dynasty leagues,
/// never lets a single week's projection swing overwhelm long-horizon value, treats temporary
/// injuries and missing signals as neutral (not destructive), and still allows a genuinely
/// unremarkable veteran to be a legitimate drop.
/// </summary>
public class DropPickupDynastyTests
{
    private static readonly Guid LeagueId = Guid.NewGuid();

    [Fact]
    public void Dynasty_And_Redraft_Produce_Different_Valuation_For_The_Same_Roster()
    {
        var youngStarter = MakePlayer(Position.WR, "Young Starter", age: 22);
        var team = MakeTeam([youngStarter.Id], starterIds: [youngStarter.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [youngStarter.Id] = MakeProjection(youngStarter.Id, points: 10, confidence: 50)
        };

        var redraft = CreateService([youngStarter], projections, team, [], LeagueType.Redraft);
        var dynasty = CreateService([youngStarter], projections, team, [], LeagueType.Dynasty);

        var redraftCandidate = Assert.Single(redraft.GetReport().RosterAssessment);
        var dynastyCandidate = Assert.Single(dynasty.GetReport().RosterAssessment);

        // Immediate value is identical (same formula, same inputs) regardless of league type.
        Assert.Equal(18.0, redraftCandidate.ImmediateValue, 3);
        Assert.Equal(18.0, dynastyCandidate.ImmediateValue, 3);

        // Only the Dynasty league computes DynastyValue and folds it into the ranking score.
        Assert.Null(redraftCandidate.DynastyValue);
        Assert.NotNull(dynastyCandidate.DynastyValue);
        Assert.Equal(16.5, dynastyCandidate.DynastyValue!.Value, 3);

        Assert.Equal(18.0, redraftCandidate.KeepValueScore, 3);
        Assert.Equal(25.5, dynastyCandidate.KeepValueScore, 3);
    }

    [Fact]
    public void Young_Starter_With_Temporary_Injury_Is_Protected_In_Dynasty()
    {
        var target = MakePlayer(Position.RB, "Young Injured Starter", age: 22);
        var filler1 = MakePlayer(Position.RB, "Filler RB 1");
        var filler2 = MakePlayer(Position.RB, "Filler RB 2");
        var freeAgent = MakePlayer(Position.RB, "Better FA RB");
        var team = MakeTeam([target.Id, filler1.Id, filler2.Id], starterIds: [target.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 3, confidence: 50), // soft week
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 8, confidence: 50)
        };
        var injuries = new Dictionary<Guid, PlayerInjuryRecord>
        {
            [target.Id] = MakeInjury(target.Id, InjurySeverity.Minor)
        };

        var service = CreateService(
            [target, filler1, filler2, freeAgent], projections, team, [], LeagueType.Dynasty, injuries);

        var candidate = service.GetReport().RosterAssessment.Single(c => c.PlayerId == target.Id);

        Assert.Equal(-3.0, candidate.ImmediateValue, 3);
        Assert.Equal(8.5, candidate.DynastyValue!.Value, 3);
        Assert.Equal(7.0, candidate.KeepValueScore, 3);
        Assert.Equal(DropPickupClassification.Hold, candidate.Classification);
    }

    [Fact]
    public void Extreme_Projection_Swing_Does_Not_Overwhelm_Dynasty_Value()
    {
        var target = MakePlayer(Position.RB, "Young Starter Bad Week", age: 22);
        var filler1 = MakePlayer(Position.RB, "Filler RB 1");
        var filler2 = MakePlayer(Position.RB, "Filler RB 2");
        var freeAgent = MakePlayer(Position.RB, "Huge Week FA RB");
        var team = MakeTeam([target.Id, filler1.Id, filler2.Id], starterIds: [target.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 0, confidence: 50),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 20, confidence: 50)
        };

        var dynasty = CreateService([target, filler1, filler2, freeAgent], projections, team, [], LeagueType.Dynasty);
        var redraft = CreateService([target, filler1, filler2, freeAgent], projections, team, [], LeagueType.Redraft);

        var dynastyCandidate = dynasty.GetReport().RosterAssessment.Single(c => c.PlayerId == target.Id);
        var redraftCandidate = redraft.GetReport().RosterAssessment.Single(c => c.PlayerId == target.Id);

        // Same brutal weekly swing (-18 immediate) is an outright Drop under Redraft...
        Assert.Equal(-18.0, redraftCandidate.KeepValueScore, 3);
        Assert.Equal(DropPickupClassification.Drop, redraftCandidate.Classification);

        // ...but Dynasty's long-horizon value keeps the same swing from being decisive.
        Assert.Equal(0.5, dynastyCandidate.KeepValueScore, 3);
        Assert.NotEqual(DropPickupClassification.Drop, dynastyCandidate.Classification);
    }

    [Fact]
    public void Older_Unremarkable_Player_Can_Still_Be_A_Legitimate_Dynasty_Drop()
    {
        var target = MakePlayer(Position.WR, "Aging Bench WR", age: 34);
        var filler1 = MakePlayer(Position.WR, "Filler WR 1");
        var filler2 = MakePlayer(Position.WR, "Filler WR 2");
        var filler3 = MakePlayer(Position.WR, "Filler WR 3");
        var freeAgent = MakePlayer(Position.WR, "Slightly Better FA WR");
        var team = MakeTeam([target.Id, filler1.Id, filler2.Id, filler3.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 4, confidence: 50),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 7, confidence: 50)
        };

        var service = CreateService(
            [target, filler1, filler2, filler3, freeAgent], projections, team, [], LeagueType.Dynasty);

        var candidate = service.GetReport().RosterAssessment.Single(c => c.PlayerId == target.Id);

        Assert.Equal(-3.0, candidate.ImmediateValue, 3);
        Assert.Equal(-6.0, candidate.DynastyValue!.Value, 3);
        Assert.Equal(-7.5, candidate.KeepValueScore, 3);
        Assert.Equal(DropPickupClassification.Drop, candidate.Classification);
    }

    [Fact]
    public void Missing_Dynasty_Signals_Contribute_Zero_Not_A_Penalty()
    {
        var target = MakePlayer(Position.TE, "Unknown Age TE", age: null);
        var filler1 = MakePlayer(Position.TE, "Filler TE 1");
        var filler2 = MakePlayer(Position.TE, "Filler TE 2");
        var freeAgent = MakePlayer(Position.TE, "FA TE");
        var team = MakeTeam([target.Id, filler1.Id, filler2.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 5, confidence: 50),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 6, confidence: 50)
        };

        // No injury record for target: GetCurrentInjury returns null (never fabricated as "healthy").
        var service = CreateService(
            [target, filler1, filler2, freeAgent], projections, team, [], LeagueType.Dynasty);

        var candidate = service.GetReport().RosterAssessment.Single(c => c.PlayerId == target.Id);

        // No age on file, not a starter, adequate depth, no injury, neutral confidence, and a
        // replacement margin too small to trigger the waiver-protection bonus: every DynastyValue
        // component is legitimately absent/neutral, so the total is exactly zero — not negative.
        Assert.Equal(0.0, candidate.DynastyValue!.Value, 3);
    }

    private static DropPickupService CreateService(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Guid, PlayerProjection> projections,
        FantasyTeam? team,
        IReadOnlyList<FantasyTeam> otherTeams,
        LeagueType leagueType,
        IReadOnlyDictionary<Guid, PlayerInjuryRecord>? injuries = null)
    {
        var league = new League
        {
            Id = LeagueId,
            Name = "Test Dynasty League",
            Platform = LeaguePlatform.Sleeper,
            LeagueType = leagueType,
            ScoringType = ScoringType.Ppr,
            NumberOfTeams = 10,
            CurrentWeek = 1,
            Season = 2026,
            IsActive = true,
            DataSource = LeagueDataSource.Sleeper,
            SelectedRosterId = team?.RosterId
        };

        var leagueState = new FakeLeagueState(
            team is null ? null : league,
            team,
            team is null ? otherTeams : [team, .. otherTeams]);
        var playerService = new FakePlayerService(players);
        var projectionService = new FakeProjectionService(projections);
        var injuryService = new FakePlayerInjuryService(injuries ?? new Dictionary<Guid, PlayerInjuryRecord>());

        return new DropPickupService(leagueState, playerService, projectionService, injuryService);
    }

    private static Player MakePlayer(Position position, string name, int? age = null) => new()
    {
        Id = Guid.NewGuid(),
        FullName = name,
        FirstName = name,
        LastName = name,
        Position = position,
        Team = "FA",
        Age = age,
        Status = PlayerStatus.Active
    };

    private static PlayerProjection MakeProjection(Guid playerId, decimal points, int confidence) => new()
    {
        PlayerId = playerId,
        Week = 1,
        ScoringFormat = ScoringType.Ppr,
        ProjectedFantasyPoints = points,
        Floor = points,
        Median = points,
        Ceiling = points,
        Confidence = confidence,
        Volatility = 30,
        ProjectionReasoning = [],
        SupportingIntelligence = [],
        ProjectionTimestamp = DateTimeOffset.UtcNow,
        ProjectionVersion = "test",
        InputsUsed = new ProjectionInputsUsed()
    };

    private static PlayerInjuryRecord MakeInjury(Guid playerId, InjurySeverity severity) => new()
    {
        PlayerId = playerId,
        Date = DateTimeOffset.UtcNow,
        Status = "Questionable",
        Severity = severity,
        IsCurrent = true
    };

    private static FantasyTeam MakeTeam(IReadOnlyList<Guid> playerIds, IReadOnlyList<Guid>? starterIds = null) => new()
    {
        LeagueId = LeagueId,
        RosterId = 1,
        DisplayName = "My Team",
        PlayerIds = playerIds,
        StarterIds = starterIds ?? []
    };

    private sealed class FakeLeagueState : ILeagueState
    {
        private readonly League? _league;
        private readonly FantasyTeam? _team;
        private readonly IReadOnlyList<FantasyTeam> _allTeams;

        public FakeLeagueState(League? league, FantasyTeam? team, IReadOnlyList<FantasyTeam> allTeams)
        {
            _league = league;
            _team = team;
            _allTeams = allTeams;
        }

        public League? CurrentLeague => _league;
        public FantasyTeam? CurrentUserTeam => _team;
        public event Action? Changed { add { } remove { } }
        public IReadOnlyList<League> GetAllLeagues() => _league is null ? [] : [_league];
        public League? GetCurrentLeague() => _league;
        public void SelectLeague(Guid leagueId)
        {
        }

        public IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId) => _allTeams;
        public IReadOnlyList<FantasyTeam> GetCurrentTeams() => _allTeams;
        public FantasyTeam? FindTeamForPlayer(Guid playerId) =>
            _allTeams.FirstOrDefault(t => t.PlayerIds.Contains(playerId));
        public FantasyTeam? GetUserTeam(Guid leagueId) => _team;
        public FantasyTeam? GetCurrentUserTeam() => _team;
        public bool SelectUserTeam(Guid leagueId, int rosterId) => false;
        public Task<LeagueConnectResult> ConnectSleeperLeagueAsync(
            string sleeperLeagueId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePlayerService : IPlayerService
    {
        private readonly IReadOnlyList<Player> _players;
        public FakePlayerService(IReadOnlyList<Player> players) => _players = players;
        public IReadOnlyList<Player> GetAllPlayers() => _players;
        public Player? GetPlayer(Guid playerId) => _players.FirstOrDefault(p => p.Id == playerId);
        public PlayerProfile? GetPlayerProfile(Guid playerId) => null;
        public IReadOnlyList<Player> SearchPlayers(string? query) => _players;
        public void Refresh()
        {
        }
    }

    private sealed class FakeProjectionService : IProjectionService
    {
        private readonly IReadOnlyDictionary<Guid, PlayerProjection> _projections;
        public FakeProjectionService(IReadOnlyDictionary<Guid, PlayerProjection> projections) =>
            _projections = projections;
        public string EngineVersion => "test";
        public PlayerProjection? GetProjection(Guid playerId) => _projections.GetValueOrDefault(playerId);
        public PlayerProjection? ProjectPlayer(Guid playerId) => GetProjection(playerId);
        public IReadOnlyList<PlayerProjection> GetAllProjections() => _projections.Values.ToList();
        public IReadOnlyList<PlayerProjection> GetTopProjections(int count = 8) =>
            _projections.Values.OrderByDescending(p => p.ProjectedFantasyPoints).Take(count).ToList();
        public PlayerProjectionComparison? ComparePlayers(Guid leftPlayerId, Guid rightPlayerId) => null;
        public IReadOnlyList<PlayerProjection> ProjectRoster(IEnumerable<Guid> playerIds) =>
            _projections.Values.Where(p => playerIds.Contains(p.PlayerId)).ToList();
        public void Refresh()
        {
        }

        public void Invalidate()
        {
        }
    }

    private sealed class FakePlayerInjuryService : Playbook.Application.Injuries.Interfaces.IPlayerInjuryService
    {
        private readonly IReadOnlyDictionary<Guid, PlayerInjuryRecord> _currentInjuries;

        public FakePlayerInjuryService(IReadOnlyDictionary<Guid, PlayerInjuryRecord> currentInjuries) =>
            _currentInjuries = currentInjuries;

        public Playbook.Application.Injuries.InjuryProviderCapabilities ActiveCapabilities =>
            Playbook.Application.Injuries.InjuryProviderCapabilities.MockCurrentOnly;
        public HistoricalDataStatus GlobalHistoricalDataStatus => HistoricalDataStatus.NotSupportedByProvider;
        public IReadOnlyList<PlayerInjuryRecord> GetAllInjuries() => _currentInjuries.Values.ToList();
        public IReadOnlyList<PlayerInjuryRecord> GetInjuriesForPlayer(Guid playerId) =>
            _currentInjuries.TryGetValue(playerId, out var record) ? [record] : [];
        public PlayerInjuryRecord? GetCurrentInjury(Guid playerId) => _currentInjuries.GetValueOrDefault(playerId);
        public IReadOnlyList<PlayerInjuryRecord> GetHistoricalInjuries(Guid playerId) => [];
        public PlayerInjuryProfile GetPlayerInjuryProfile(Guid playerId) => new() { PlayerId = playerId };
        public void Refresh()
        {
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
