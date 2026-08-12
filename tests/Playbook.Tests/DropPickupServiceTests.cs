using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Infrastructure.Intelligence.Services;

namespace Playbook.Tests;

public class DropPickupServiceTests
{
    private static readonly Guid LeagueId = Guid.NewGuid();

    [Fact]
    public void Suggests_A_SamePosition_Swap_When_A_Free_Agent_Projects_Higher()
    {
        var weakWr = MakePlayer(Position.WR, "Weak WR");
        var strongWr = MakePlayer(Position.WR, "Strong WR");
        var players = new[] { weakWr, strongWr };
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [weakWr.Id] = MakeProjection(weakWr.Id, points: 5, confidence: 50),
            [strongWr.Id] = MakeProjection(strongWr.Id, points: 12, confidence: 60)
        };
        var team = MakeTeam([weakWr.Id]);
        var service = CreateService(players, projections, team, otherTeams: []);

        var report = service.GetReport();

        var suggestion = Assert.Single(report.Suggestions);
        Assert.Equal(weakWr.Id, suggestion.Drop.PlayerId);
        Assert.Equal(strongWr.Id, suggestion.Pickup.PlayerId);
        Assert.Equal(7.0, suggestion.ValueGain, 3);
    }

    [Fact]
    public void No_Suggestion_When_No_Available_Player_Improves_On_The_Roster()
    {
        var bestWr = MakePlayer(Position.WR, "Best WR");
        var worseWr = MakePlayer(Position.WR, "Worse Available WR");
        var team = MakeTeam([bestWr.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [bestWr.Id] = MakeProjection(bestWr.Id, points: 15, confidence: 70),
            [worseWr.Id] = MakeProjection(worseWr.Id, points: 4, confidence: 40)
        };
        var service = CreateService([bestWr, worseWr], projections, team, otherTeams: []);

        var report = service.GetReport();

        Assert.Empty(report.Suggestions);
    }

    [Fact]
    public void Replacement_Margin_Is_Own_Projection_Minus_Best_Available_SamePosition()
    {
        var rb = MakePlayer(Position.RB, "Roster RB");
        var fa1 = MakePlayer(Position.RB, "FA RB Low");
        var fa2 = MakePlayer(Position.RB, "FA RB High");
        var team = MakeTeam([rb.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [rb.Id] = MakeProjection(rb.Id, points: 5, confidence: 50),
            [fa1.Id] = MakeProjection(fa1.Id, points: 3, confidence: 50),
            [fa2.Id] = MakeProjection(fa2.Id, points: 9, confidence: 50)
        };
        var service = CreateService([rb, fa1, fa2], projections, team, otherTeams: []);

        var report = service.GetReport();

        // Best available RB is fa2 (9 pts): margin = 5 - 9 = -4.
        var suggestion = Assert.Single(report.Suggestions);
        Assert.Equal(-4.0, suggestion.Drop.ReplacementMargin);
        Assert.Equal(fa2.Id, suggestion.Pickup.PlayerId);
    }

    [Fact]
    public void Redundant_Position_Ranks_As_The_Weaker_Keep_Over_An_Identical_Thin_Position_Margin()
    {
        // Lone TE and WR3 both have the exact same -2 replacement margin (an available free
        // agent projects 2 pts higher at their position), but WR3 is the third WR on the roster
        // while the TE is the only one — positional depth should make WR3 the higher-priority
        // drop even though the raw projection math is identical.
        var loneTe = MakePlayer(Position.TE, "Lone TE");
        var wr1 = MakePlayer(Position.WR, "WR1 Locked In");
        var wr2 = MakePlayer(Position.WR, "WR2 Locked In");
        var wr3 = MakePlayer(Position.WR, "WR3 Redundant");
        var faTe = MakePlayer(Position.TE, "FA TE");
        var faWr = MakePlayer(Position.WR, "FA WR");
        var team = MakeTeam([loneTe.Id, wr1.Id, wr2.Id, wr3.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [loneTe.Id] = MakeProjection(loneTe.Id, points: 6, confidence: 50),
            [wr1.Id] = MakeProjection(wr1.Id, points: 20, confidence: 60),
            [wr2.Id] = MakeProjection(wr2.Id, points: 18, confidence: 60),
            [wr3.Id] = MakeProjection(wr3.Id, points: 6, confidence: 50),
            [faTe.Id] = MakeProjection(faTe.Id, points: 8, confidence: 50),
            [faWr.Id] = MakeProjection(faWr.Id, points: 8, confidence: 50)
        };
        var service = CreateService(
            [loneTe, wr1, wr2, wr3, faTe, faWr], projections, team, otherTeams: []);

        var report = service.GetReport();

        Assert.True(report.Suggestions.Count >= 2);
        // Identical -2 replacement margin on both, but WR3 (redundant) must be prioritized
        // ahead of the lone TE (scarcity-protected) as the drop candidate.
        Assert.Equal(wr3.Id, report.Suggestions[0].Drop.PlayerId);
        Assert.Equal(loneTe.Id, report.Suggestions[1].Drop.PlayerId);
        Assert.Equal(
            report.Suggestions[0].Drop.ReplacementMargin,
            report.Suggestions[1].Drop.ReplacementMargin);
    }

    [Fact]
    public void Players_Rostered_By_Other_Teams_Are_Never_Suggested_As_Pickups()
    {
        var weakWr = MakePlayer(Position.WR, "Weak WR");
        var betterButOwned = MakePlayer(Position.WR, "Owned By Rival");
        var team = MakeTeam([weakWr.Id]);
        var rival = new FantasyTeam
        {
            LeagueId = LeagueId,
            RosterId = 2,
            DisplayName = "Rival",
            PlayerIds = [betterButOwned.Id]
        };
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [weakWr.Id] = MakeProjection(weakWr.Id, points: 5, confidence: 50),
            [betterButOwned.Id] = MakeProjection(betterButOwned.Id, points: 20, confidence: 80)
        };
        var service = CreateService([weakWr, betterButOwned], projections, team, otherTeams: [rival]);

        var report = service.GetReport();

        Assert.Empty(report.Suggestions);
        Assert.Equal(0, report.AvailablePlayerCount);
    }

    [Fact]
    public void Report_Is_Deterministic_Across_Rebuilds()
    {
        var weakWr = MakePlayer(Position.WR, "Weak WR");
        var strongWr = MakePlayer(Position.WR, "Strong WR");
        var team = MakeTeam([weakWr.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [weakWr.Id] = MakeProjection(weakWr.Id, points: 5, confidence: 50),
            [strongWr.Id] = MakeProjection(strongWr.Id, points: 12, confidence: 60)
        };

        var serviceA = CreateService([weakWr, strongWr], projections, team, otherTeams: []);
        var serviceB = CreateService([weakWr, strongWr], projections, team, otherTeams: []);

        var reportA = serviceA.GetReport();
        var reportB = serviceB.GetReport();

        Assert.Equal(reportA.Suggestions.Count, reportB.Suggestions.Count);
        Assert.Equal(reportA.Suggestions[0].Drop.PlayerId, reportB.Suggestions[0].Drop.PlayerId);
        Assert.Equal(reportA.Suggestions[0].Pickup.PlayerId, reportB.Suggestions[0].Pickup.PlayerId);
        Assert.Equal(reportA.Suggestions[0].ValueGain, reportB.Suggestions[0].ValueGain);
    }

    [Fact]
    public void Empty_Report_When_No_Team_Selected()
    {
        var service = CreateService([], new Dictionary<Guid, PlayerProjection>(), team: null, otherTeams: []);

        var report = service.GetReport();

        Assert.Empty(report.Suggestions);
        Assert.False(report.HasRosterPlayers);
    }

    [Fact]
    public void Dynasty_Protects_Young_EarlyCareer_Player_From_Small_Projection_Edge()
    {
        // Young RB with early-career years: a modest FA projection edge must not auto-drop them.
        var youngRb = MakePlayer(Position.RB, "Young Upside RB", age: 23, yearsPro: 1);
        var olderFa = MakePlayer(Position.RB, "Older FA RB", age: 29, yearsPro: 7);
        var team = MakeTeam([youngRb.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [youngRb.Id] = MakeProjection(youngRb.Id, points: 8, confidence: 40),
            [olderFa.Id] = MakeProjection(olderFa.Id, points: 12, confidence: 55)
        };
        var service = CreateService(
            [youngRb, olderFa], projections, team, otherTeams: [], leagueType: LeagueType.Dynasty);

        var report = service.GetReport();

        Assert.Empty(report.Suggestions);
    }

    [Fact]
    public void Dynasty_Still_Recommends_Dropping_Older_LowUpside_Player()
    {
        var agingRb = MakePlayer(Position.RB, "Aging RB", age: 30, yearsPro: 8);
        var betterFa = MakePlayer(Position.RB, "Better Available RB", age: 26, yearsPro: 4);
        var team = MakeTeam([agingRb.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [agingRb.Id] = MakeProjection(agingRb.Id, points: 5, confidence: 50),
            [betterFa.Id] = MakeProjection(betterFa.Id, points: 12, confidence: 60)
        };
        var service = CreateService(
            [agingRb, betterFa], projections, team, otherTeams: [], leagueType: LeagueType.Dynasty);

        var report = service.GetReport();

        var suggestion = Assert.Single(report.Suggestions);
        Assert.Equal(agingRb.Id, suggestion.Drop.PlayerId);
        Assert.Equal(betterFa.Id, suggestion.Pickup.PlayerId);
        Assert.True(suggestion.Drop.DynastyKeepAdjustment < 0);
        Assert.Contains(suggestion.Drop.Reasons, r => r.Contains("aging", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dynasty_Missing_Age_And_YearsPro_Does_Not_Break_Recommendations()
    {
        var unknownWr = MakePlayer(Position.WR, "Unknown Meta WR"); // age/yearsPro null
        var faWr = MakePlayer(Position.WR, "Available WR");
        var team = MakeTeam([unknownWr.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [unknownWr.Id] = MakeProjection(unknownWr.Id, points: 4, confidence: 50),
            [faWr.Id] = MakeProjection(faWr.Id, points: 11, confidence: 55)
        };
        var service = CreateService(
            [unknownWr, faWr], projections, team, otherTeams: [], leagueType: LeagueType.Dynasty);

        var report = service.GetReport();

        var suggestion = Assert.Single(report.Suggestions);
        Assert.Equal(unknownWr.Id, suggestion.Drop.PlayerId);
        Assert.Equal(0, suggestion.Drop.DynastyKeepAdjustment);
        Assert.DoesNotContain(
            suggestion.Drop.Reasons,
            r => r.Contains("Dynasty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Redraft_Ignores_Dynasty_Keep_Adjustment()
    {
        var youngRb = MakePlayer(Position.RB, "Young Upside RB", age: 23, yearsPro: 1);
        var olderFa = MakePlayer(Position.RB, "Older FA RB", age: 29, yearsPro: 7);
        var team = MakeTeam([youngRb.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [youngRb.Id] = MakeProjection(youngRb.Id, points: 8, confidence: 40),
            [olderFa.Id] = MakeProjection(olderFa.Id, points: 12, confidence: 55)
        };
        var service = CreateService(
            [youngRb, olderFa], projections, team, otherTeams: [], leagueType: LeagueType.Redraft);

        var report = service.GetReport();

        var suggestion = Assert.Single(report.Suggestions);
        Assert.Equal(youngRb.Id, suggestion.Drop.PlayerId);
        Assert.Equal(0, suggestion.Drop.DynastyKeepAdjustment);
    }

    private static DropPickupService CreateService(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Guid, PlayerProjection> projections,
        FantasyTeam? team,
        IReadOnlyList<FantasyTeam> otherTeams,
        LeagueType leagueType = LeagueType.Redraft)
    {
        var league = new League
        {
            Id = LeagueId,
            Name = "Test League",
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

        return new DropPickupService(leagueState, playerService, projectionService);
    }

    private static Player MakePlayer(
        Position position,
        string name,
        int? age = null,
        int? yearsPro = null,
        PlayerStatus status = PlayerStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        FullName = name,
        FirstName = name,
        LastName = name,
        Position = position,
        Team = "FA",
        Status = status,
        Age = age,
        YearsPro = yearsPro
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

    private static FantasyTeam MakeTeam(IReadOnlyList<Guid> playerIds) => new()
    {
        LeagueId = LeagueId,
        RosterId = 1,
        DisplayName = "My Team",
        PlayerIds = playerIds,
        StarterIds = []
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
}
