using Playbook.Application.Draft;
using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Draft;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Infrastructure.Draft;
using Playbook.Infrastructure.Players;

namespace Playbook.Tests;

public class DraftAssistantServiceTests
{
    [Theory]
    [InlineData("pre_draft", DraftStatus.NotStarted)]
    [InlineData("drafting", DraftStatus.Drafting)]
    [InlineData("DRAFTING", DraftStatus.Drafting)]
    [InlineData("paused", DraftStatus.Paused)]
    [InlineData("complete", DraftStatus.Complete)]
    [InlineData("something_unrecognized", DraftStatus.Unknown)]
    public void ParseStatus_Maps_Real_Sleeper_Status_Strings(string raw, DraftStatus expected)
    {
        Assert.Equal(expected, DraftAssistantService.ParseStatus(raw));
    }

    [Fact]
    public void BuildSlotToRosterId_Cross_References_DraftOrder_Against_Real_Roster_Ownership()
    {
        var draft = MakeDraftSnapshot(new Dictionary<string, int> { ["user-1"] = 1, ["user-2"] = 2 });
        var snapshot = MakeLeagueSnapshot(MakeRoster(10, "user-1"), MakeRoster(20, "user-2"));

        var result = DraftAssistantService.BuildSlotToRosterId(draft, snapshot);

        Assert.Equal(10, result[1]);
        Assert.Equal(20, result[2]);
    }

    [Fact]
    public void BuildSlotToRosterId_Empty_When_League_Snapshot_Unavailable()
    {
        var draft = MakeDraftSnapshot(new Dictionary<string, int> { ["user-1"] = 1 });

        var result = DraftAssistantService.BuildSlotToRosterId(draft, leagueSnapshot: null);

        Assert.Empty(result);
    }

    [Fact]
    public void CountPositionSlots_Counts_Direct_Slots_Plus_Flex_Share()
    {
        var rosterPositions = new[] { "QB", "RB", "WR", "TE", "FLEX" };

        Assert.Equal(1, DraftAssistantService.CountPositionSlots(rosterPositions, Position.QB));
        Assert.Equal(2, DraftAssistantService.CountPositionSlots(rosterPositions, Position.RB));
        Assert.Equal(2, DraftAssistantService.CountPositionSlots(rosterPositions, Position.WR));
        Assert.Equal(2, DraftAssistantService.CountPositionSlots(rosterPositions, Position.TE));
    }

    [Fact]
    public void BuildRosterNeeds_Marks_Understaffed_Positions_Urgent_And_Overstaffed_Satisfied()
    {
        var league = MakeLeague(rosterPositions: ["QB", "RB", "WR", "TE"]);
        var drafted = new List<Player>
        {
            MakePlayer(Position.RB, "RB1"),
            MakePlayer(Position.RB, "RB2")
        };

        var needs = DraftAssistantService.BuildRosterNeeds(league, drafted);

        Assert.Equal(PositionalNeedLevel.Satisfied, needs.Single(n => n.PositionLabel == "RB").NeedLevel);
        Assert.Equal(PositionalNeedLevel.Urgent, needs.Single(n => n.PositionLabel == "WR").NeedLevel);
        Assert.Equal(PositionalNeedLevel.Urgent, needs.Single(n => n.PositionLabel == "QB").NeedLevel);
    }

    [Fact]
    public void ComputeReplacementLevels_Uses_Projection_At_The_Real_Starter_Cutoff()
    {
        var league = MakeLeague(numberOfTeams: 2, rosterPositions: ["RB"]);
        var rbHigh = MakePlayer(Position.RB, "RB High");
        var rbMid = MakePlayer(Position.RB, "RB Mid");
        var rbLow = MakePlayer(Position.RB, "RB Low");
        var undrafted = new List<Player> { rbHigh, rbMid, rbLow };
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [rbHigh.Id] = MakeProjection(rbHigh.Id, points: 20, confidence: 70),
            [rbMid.Id] = MakeProjection(rbMid.Id, points: 12, confidence: 60),
            [rbLow.Id] = MakeProjection(rbLow.Id, points: 5, confidence: 50)
        };

        var levels = DraftAssistantService.ComputeReplacementLevels(undrafted, projections, league);

        // 1 starter slot * 2 teams => replacement is the 2nd-best remaining RB (rbMid, 12 pts).
        Assert.Equal(12m, levels[Position.RB]);
    }

    [Fact]
    public void ScorePlayer_Flags_Positive_Scarcity_Factor_When_Well_Above_Replacement_Level()
    {
        var player = MakePlayer(Position.RB, "Scarce RB");
        var projection = MakeProjection(player.Id, points: 20, confidence: 60);

        var candidate = DraftAssistantService.ScorePlayer(
            player, projection,
            new Dictionary<Position, decimal> { [Position.RB] = 10m },
            new Dictionary<string, PositionalNeedLevel>(),
            isDynasty: false,
            currentInjury: null);

        Assert.Equal(10m, candidate.ValueOverReplacement);
        var scarcity = candidate.Factors.Single(f => f.Label == "Positional scarcity");
        Assert.Equal(FactorDirection.Positive, scarcity.Direction);
    }

    [Fact]
    public void ScorePlayer_Flags_Negative_Scarcity_Factor_When_Below_Replacement_Level()
    {
        var player = MakePlayer(Position.RB, "Deep Position RB");
        var projection = MakeProjection(player.Id, points: 6, confidence: 40);

        var candidate = DraftAssistantService.ScorePlayer(
            player, projection,
            new Dictionary<Position, decimal> { [Position.RB] = 10m },
            new Dictionary<string, PositionalNeedLevel>(),
            isDynasty: false,
            currentInjury: null);

        Assert.Equal(-4m, candidate.ValueOverReplacement);
        var scarcity = candidate.Factors.Single(f => f.Label == "Positional scarcity");
        Assert.Equal(FactorDirection.Negative, scarcity.Direction);
    }

    [Fact]
    public void ScorePlayer_Applies_Injury_Penalty_And_Reduces_Confidence()
    {
        var player = MakePlayer(Position.WR, "Injured WR");
        var projection = MakeProjection(player.Id, points: 15, confidence: 60);
        var injury = new PlayerInjuryRecord
        {
            PlayerId = player.Id,
            Date = DateTimeOffset.UtcNow,
            Status = "Out",
            Severity = InjurySeverity.Major
        };

        var healthy = DraftAssistantService.ScorePlayer(
            player, projection, new Dictionary<Position, decimal>(), new Dictionary<string, PositionalNeedLevel>(),
            isDynasty: false, currentInjury: null);
        var injured = DraftAssistantService.ScorePlayer(
            player, projection, new Dictionary<Position, decimal>(), new Dictionary<string, PositionalNeedLevel>(),
            isDynasty: false, currentInjury: injury);

        Assert.True(injured.TeamFitScore < healthy.TeamFitScore);
        Assert.True(injured.ProjectionConfidence < healthy.ProjectionConfidence);
    }

    [Fact]
    public async Task GetReportAsync_Returns_NotConnected_State_When_No_League_Selected()
    {
        var service = CreateService(league: null, team: null, sleeper: new FakeSleeperLeagueClient());

        var report = await service.GetReportAsync();

        Assert.Null(report.Board);
        Assert.False(report.IsStale);
        Assert.False(report.IsOnTheClock);
    }

    [Fact]
    public async Task GetReportAsync_Marks_Stale_Rather_Than_Fabricating_When_Sleeper_Cannot_List_Drafts()
    {
        var league = MakeLeague();
        var sleeper = new FakeSleeperLeagueClient { ThrowOnListDrafts = true };
        var service = CreateService(league, MakeTeam(100), sleeper);

        var report = await service.GetReportAsync();

        Assert.Null(report.Board);
        Assert.True(report.IsStale);
    }

    [Fact]
    public async Task GetReportAsync_Marks_Stale_Rather_Than_Fabricating_When_Sleeper_Cannot_Fetch_Draft_Details()
    {
        var league = MakeLeague();
        var sleeper = new FakeSleeperLeagueClient
        {
            Drafts = [new SleeperDraftSummary { DraftId = "d1", Status = "drafting", Season = "2026" }],
            ThrowOnDraftFetch = true
        };
        var service = CreateService(league, MakeTeam(100), sleeper);

        var report = await service.GetReportAsync();

        Assert.Null(report.Board);
        Assert.True(report.IsStale);
    }

    [Fact]
    public async Task GetReportAsync_Marks_Stale_When_Draft_Details_Come_Back_Null()
    {
        var league = MakeLeague();
        var sleeper = new FakeSleeperLeagueClient
        {
            Drafts = [new SleeperDraftSummary { DraftId = "d1", Status = "drafting", Season = "2026" }],
            Draft = null
        };
        var service = CreateService(league, MakeTeam(100), sleeper);

        var report = await service.GetReportAsync();

        Assert.Null(report.Board);
        Assert.True(report.IsStale);
    }

    [Fact]
    public async Task GetReportAsync_Reports_No_Draft_Found_Without_Fabricating_One()
    {
        var league = MakeLeague();
        var sleeper = new FakeSleeperLeagueClient { Drafts = [] };
        var service = CreateService(league, MakeTeam(100), sleeper);

        var report = await service.GetReportAsync();

        Assert.Null(report.Board);
        Assert.False(report.IsStale);
        Assert.Contains("No draft found", report.StatusMessage);
    }

    [Fact]
    public async Task GetReportAsync_Shows_Idle_State_When_Draft_Has_Not_Started()
    {
        var league = MakeLeague();
        var sleeper = new FakeSleeperLeagueClient
        {
            Drafts = [new SleeperDraftSummary { DraftId = "d1", Status = "pre_draft", Season = "2026" }],
            Draft = MakeDraftSnapshot(new Dictionary<string, int>(), status: "pre_draft"),
            Picks = [],
            LeagueSnapshot = MakeLeagueSnapshot(MakeRoster(100, "user-me"), MakeRoster(200, "user-rival"))
        };
        var service = CreateService(league, MakeTeam(100), sleeper);

        var report = await service.GetReportAsync();

        Assert.NotNull(report.Board);
        Assert.False(report.IsOnTheClock);
        Assert.Null(report.Recommended);
        Assert.Equal("Draft has not started yet.", report.StatusMessage);
    }

    [Fact]
    public async Task GetReportAsync_IsOnTheClock_True_When_Next_Slot_Resolves_To_The_Users_Roster()
    {
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        var service = CreateService(league, team, sleeper);

        var report = await service.GetReportAsync();

        Assert.True(report.IsOnTheClock);
        Assert.Equal("You're on the clock.", report.StatusMessage);
    }

    [Fact]
    public async Task GetReportAsync_IsOnTheClock_False_When_Its_Another_Teams_Turn()
    {
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: false);
        var service = CreateService(league, team, sleeper);

        var report = await service.GetReportAsync();

        Assert.False(report.IsOnTheClock);
    }

    [Fact]
    public async Task GetReportAsync_Reports_Complete_Draft_Without_Recommendations()
    {
        var league = MakeLeague(numberOfTeams: 2, rosterPositions: ["QB", "RB"]);
        var picks = Enumerable.Range(1, 6)
            .Select(i => new SleeperDraftPickSnapshot
            {
                PickNumber = i,
                Round = ((i - 1) / 2) + 1,
                DraftSlot = ((i - 1) % 2) + 1,
                RosterId = 100,
                SleeperPlayerId = $"s-{i}"
            })
            .ToList();
        var sleeper = new FakeSleeperLeagueClient
        {
            Drafts = [new SleeperDraftSummary { DraftId = "d1", Status = "complete", Season = "2026" }],
            Draft = MakeDraftSnapshot(new Dictionary<string, int>(), status: "complete", rounds: 3, teams: 2),
            Picks = picks,
            LeagueSnapshot = MakeLeagueSnapshot(MakeRoster(100, "user-me"), MakeRoster(200, "user-rival"))
        };
        var service = CreateService(league, MakeTeam(100), sleeper);

        var report = await service.GetReportAsync();

        Assert.NotNull(report.Board);
        Assert.True(report.Board!.IsComplete);
        Assert.Null(report.Recommended);
        Assert.Equal("This draft is complete.", report.StatusMessage);
    }

    /// <summary>
    /// Regression for a defect found validating against a real Sleeper dynasty league whose draft
    /// had already completed: IsDynasty/Strategy/Phase/LeagueId were only ever set on the
    /// active-drafting report, so a dynasty league defaulted to
    /// IsDynasty=false/Strategy=Hybrid/LeagueId=null the moment its draft was complete — hiding
    /// the strategy selector for the exact league it exists for. Same bug applied to the
    /// not-started/paused early-return path.
    /// </summary>
    [Fact]
    public async Task GetReportAsync_Reports_DynastyMetadata_OnTheCompleteDraft_EarlyReturnPath()
    {
        var league = MakeLeague(
            numberOfTeams: 2, rosterPositions: ["QB", "RB"], leagueType: LeagueType.Dynasty);
        var picks = Enumerable.Range(1, 6)
            .Select(i => new SleeperDraftPickSnapshot
            {
                PickNumber = i,
                Round = ((i - 1) / 2) + 1,
                DraftSlot = ((i - 1) % 2) + 1,
                RosterId = 100,
                SleeperPlayerId = $"s-{i}"
            })
            .ToList();
        var sleeper = new FakeSleeperLeagueClient
        {
            Drafts = [new SleeperDraftSummary { DraftId = "d1", Status = "complete", Season = "2026" }],
            Draft = MakeDraftSnapshot(new Dictionary<string, int>(), status: "complete", rounds: 3, teams: 2),
            Picks = picks,
            LeagueSnapshot = MakeLeagueSnapshot(MakeRoster(100, "user-me"), MakeRoster(200, "user-rival"))
        };
        var service = CreateService(league, MakeTeam(100), sleeper);

        var report = await service.GetReportAsync();

        Assert.True(report.Board!.IsComplete);
        Assert.True(report.IsDynasty, "a dynasty league must still report IsDynasty on the complete-draft path");
        Assert.Equal(league.Id, report.LeagueId);
        Assert.Equal(DynastyStrategy.Hybrid, report.Strategy); // default until explicitly changed
    }

    /// <summary>Same defect class, the not-started early-return path.</summary>
    [Fact]
    public async Task GetReportAsync_Reports_DynastyMetadata_OnTheNotStartedDraft_EarlyReturnPath()
    {
        var league = MakeLeague(
            numberOfTeams: 2, rosterPositions: ["QB", "RB"], leagueType: LeagueType.Dynasty);
        var sleeper = new FakeSleeperLeagueClient
        {
            Drafts = [new SleeperDraftSummary { DraftId = "d1", Status = "pre_draft", Season = "2026" }],
            Draft = MakeDraftSnapshot(new Dictionary<string, int>(), status: "pre_draft", rounds: 3, teams: 2),
            Picks = [],
            LeagueSnapshot = MakeLeagueSnapshot(MakeRoster(100, "user-me"), MakeRoster(200, "user-rival"))
        };
        var service = CreateService(league, MakeTeam(100), sleeper);

        var report = await service.GetReportAsync();

        Assert.Equal("Draft has not started yet.", report.StatusMessage);
        Assert.True(report.IsDynasty, "a dynasty league must still report IsDynasty before its draft starts");
        Assert.Equal(league.Id, report.LeagueId);
    }

    [Fact]
    public async Task GetReportAsync_Excludes_Drafted_Players_And_Ranks_By_Team_Fit_Not_Just_Raw_Projection()
    {
        var league = MakeLeague(numberOfTeams: 2, rosterPositions: ["QB", "RB", "WR", "TE"]);
        var team = MakeTeam(100);

        var rbOwned1 = MakeSleeperMappedPlayer(Position.RB, "Owned RB1", "s-rb1");
        var rbOwned2 = MakeSleeperMappedPlayer(Position.RB, "Owned RB2", "s-rb2");
        var rivalTe = MakeSleeperMappedPlayer(Position.TE, "Rival TE", "s-te");
        var rivalQb = MakeSleeperMappedPlayer(Position.QB, "Rival QB (Highest Raw Value)", "s-qb");
        var candidateRb = MakeSleeperMappedPlayer(Position.RB, "Undrafted RB", "s-rb-candidate");
        var candidateWr = MakeSleeperMappedPlayer(Position.WR, "Undrafted WR", "s-wr-candidate");

        var players = new List<Player> { rbOwned1, rbOwned2, rivalTe, rivalQb, candidateRb, candidateWr };
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [candidateRb.Id] = MakeProjection(candidateRb.Id, points: 20, confidence: 60),
            [candidateWr.Id] = MakeProjection(candidateWr.Id, points: 17, confidence: 55),
            [rivalQb.Id] = MakeProjection(rivalQb.Id, points: 99, confidence: 80)
        };

        var sleeper = new FakeSleeperLeagueClient
        {
            Drafts = [new SleeperDraftSummary { DraftId = "d1", Status = "drafting", Season = "2026" }],
            Draft = MakeDraftSnapshot(
                new Dictionary<string, int> { ["user-me"] = 1, ["user-rival"] = 2 },
                status: "drafting", rounds: 3, teams: 2),
            Picks =
            [
                new SleeperDraftPickSnapshot { PickNumber = 1, Round = 1, DraftSlot = 1, RosterId = 100, SleeperPlayerId = "s-rb1" },
                new SleeperDraftPickSnapshot { PickNumber = 2, Round = 1, DraftSlot = 2, RosterId = 200, SleeperPlayerId = "s-te" },
                new SleeperDraftPickSnapshot { PickNumber = 3, Round = 2, DraftSlot = 2, RosterId = 100, SleeperPlayerId = "s-rb2" },
                new SleeperDraftPickSnapshot { PickNumber = 4, Round = 2, DraftSlot = 1, RosterId = 200, SleeperPlayerId = "s-qb" }
            ],
            LeagueSnapshot = MakeLeagueSnapshot(MakeRoster(100, "user-me"), MakeRoster(200, "user-rival"))
        };

        var service = CreateService(league, team, sleeper, players, projections);

        var report = await service.GetReportAsync();

        Assert.NotNull(report.Board);
        Assert.Equal(4, report.Board!.Picks.Count(p => p.IsMade));

        // Roster construction, straight from the real drafted picks: 2 RBs already rostered
        // (Satisfied), no WR drafted yet (Urgent).
        Assert.Equal(PositionalNeedLevel.Satisfied, report.RosterNeeds.Single(n => n.PositionLabel == "RB").NeedLevel);
        Assert.Equal(PositionalNeedLevel.Urgent, report.RosterNeeds.Single(n => n.PositionLabel == "WR").NeedLevel);

        var recommendedIds = new[] { report.Recommended }
            .Concat(report.Alternatives)
            .Where(r => r is not null)
            .Select(r => r!.PlayerId)
            .ToList();

        // Drafted-player removal: the highest-raw-projection player in the pool was already
        // taken (by the rival) and must never be recommended, no matter how good the projection.
        Assert.DoesNotContain(rivalQb.Id, recommendedIds);
        Assert.DoesNotContain(rbOwned1.Id, recommendedIds);
        Assert.DoesNotContain(rbOwned2.Id, recommendedIds);
        Assert.DoesNotContain(rivalTe.Id, recommendedIds);

        var rbRec = recommendedIds.Contains(candidateRb.Id)
            ? (report.Recommended!.PlayerId == candidateRb.Id ? report.Recommended : report.Alternatives.Single(a => a.PlayerId == candidateRb.Id))
            : null;
        Assert.NotNull(rbRec);

        // Recommendation ranking: raw projection alone favors the RB (20 pts > 17 pts).
        Assert.Equal(1, rbRec!.BestPlayerAvailableRank);

        // Roster-aware recommendation: the roster already has surplus RB depth and zero WRs, so
        // the lower-projected WR is the correct team-fit pick even though it isn't the best raw
        // player available.
        Assert.NotNull(report.Recommended);
        Assert.Equal(candidateWr.Id, report.Recommended!.PlayerId);
        Assert.Equal(1, report.Recommended.TeamFitRank);
        Assert.Equal(2, rbRec.TeamFitRank);
    }

    private static (League league, FakeSleeperLeagueClient sleeper, FantasyTeam team) BuildOnTheClockScenario(
        bool nextPickGoesToUser)
    {
        var league = MakeLeague();
        var team = MakeTeam(100);
        var userSlot = nextPickGoesToUser ? 1 : 2;
        var rivalSlot = nextPickGoesToUser ? 2 : 1;

        var sleeper = new FakeSleeperLeagueClient
        {
            Drafts = [new SleeperDraftSummary { DraftId = "d1", Status = "drafting", Season = "2026" }],
            Draft = MakeDraftSnapshot(
                new Dictionary<string, int> { ["user-me"] = userSlot, ["user-rival"] = rivalSlot },
                status: "drafting", rounds: 3, teams: 2),
            Picks = [],
            LeagueSnapshot = MakeLeagueSnapshot(MakeRoster(100, "user-me"), MakeRoster(200, "user-rival"))
        };

        return (league, sleeper, team);
    }

    private static DraftAssistantService CreateService(
        League? league,
        FantasyTeam? team,
        FakeSleeperLeagueClient sleeper,
        IReadOnlyList<Player>? players = null,
        IReadOnlyDictionary<Guid, PlayerProjection>? projections = null,
        IReadOnlyDictionary<Guid, PlayerInjuryRecord>? injuries = null)
    {
        var leagueState = new FakeLeagueState(league, team);
        var playerService = new FakePlayerService(players ?? []);
        var projectionService = new FakeProjectionService(projections ?? new Dictionary<Guid, PlayerProjection>());
        var injuryService = new FakePlayerInjuryService(injuries ?? new Dictionary<Guid, PlayerInjuryRecord>());

        return new DraftAssistantService(
            leagueState, sleeper, playerService, projectionService, injuryService,
            new FakeByeWeekProvider(),
            NullLogger<DraftAssistantService>.Instance);
    }

    private static League MakeLeague(
        int numberOfTeams = 2,
        IReadOnlyList<string>? rosterPositions = null,
        LeagueType leagueType = LeagueType.Redraft) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test League",
        Platform = LeaguePlatform.Sleeper,
        LeagueType = leagueType,
        ScoringType = ScoringType.Ppr,
        NumberOfTeams = numberOfTeams,
        CurrentWeek = 1,
        Season = 2026,
        IsActive = true,
        DataSource = LeagueDataSource.Sleeper,
        ExternalId = "lg1",
        RosterPositions = rosterPositions ?? ["QB", "RB", "WR", "TE"]
    };

    private static FantasyTeam MakeTeam(int rosterId) => new()
    {
        LeagueId = Guid.NewGuid(),
        RosterId = rosterId,
        DisplayName = "My Team",
        PlayerIds = [],
        StarterIds = []
    };

    private static Player MakePlayer(Position position, string name) => new()
    {
        Id = Guid.NewGuid(),
        FullName = name,
        FirstName = name,
        LastName = name,
        Position = position,
        Team = "FA",
        Status = PlayerStatus.Active
    };

    private static Player MakeSleeperMappedPlayer(Position position, string name, string sleeperId) => new()
    {
        Id = SleeperPlayerIds.ToPlaybookId(sleeperId),
        FullName = name,
        FirstName = name,
        LastName = name,
        Position = position,
        Team = "FA",
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

    private static SleeperDraftSnapshot MakeDraftSnapshot(
        IReadOnlyDictionary<string, int> draftOrder,
        string status = "drafting",
        int rounds = 3,
        int teams = 2) => new()
    {
        DraftId = "d1",
        LeagueId = "lg1",
        Season = "2026",
        Status = status,
        Type = "snake",
        Rounds = rounds,
        Teams = teams,
        DraftOrderByUserId = draftOrder
    };

    private static SleeperLeagueSnapshot MakeLeagueSnapshot(params SleeperRosterSnapshot[] rosters) => new()
    {
        ExternalLeagueId = "lg1",
        Name = "Test League",
        Season = "2026",
        Status = "in_season",
        TeamCount = rosters.Length,
        CurrentWeek = 1,
        SleeperLeagueType = 2,
        ScoringSettings = new Dictionary<string, double>(),
        RosterPositions = ["QB", "RB", "WR", "TE"],
        Rosters = rosters
    };

    private static SleeperRosterSnapshot MakeRoster(int rosterId, string ownerId) => new()
    {
        RosterId = rosterId,
        OwnerId = ownerId,
        TeamName = $"Team {rosterId}",
        OwnerName = ownerId,
        SleeperPlayerIds = [],
        StarterSleeperPlayerIds = [],
        ReserveSleeperPlayerIds = [],
        TaxiSleeperPlayerIds = []
    };

    /// <summary>
    /// No schedule loaded, so the bye-week factor reports itself unavailable. Keeps these tests
    /// focused on the behaviour they were originally written for.
    /// </summary>
    private sealed class FakeByeWeekProvider : IByeWeekProvider
    {
        public ByeWeekMap GetByeWeeks(int season) => ByeWeekMap.Empty;

        public Task RefreshAsync(int season, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeLeagueState : ILeagueState
    {
        private readonly League? _league;
        private readonly FantasyTeam? _team;

        public FakeLeagueState(League? league, FantasyTeam? team)
        {
            _league = league;
            _team = team;
        }

        public League? CurrentLeague => _league;
        public FantasyTeam? CurrentUserTeam => _team;
        public event Action? Changed { add { } remove { } }
        public IReadOnlyList<League> GetAllLeagues() => _league is null ? [] : [_league];
        public League? GetCurrentLeague() => _league;
        public void SelectLeague(Guid leagueId)
        {
        }

        public IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId) => _team is null ? [] : [_team];
        public IReadOnlyList<FantasyTeam> GetCurrentTeams() => _team is null ? [] : [_team];
        public FantasyTeam? FindTeamForPlayer(Guid playerId) => null;
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

    private sealed class FakePlayerInjuryService : IPlayerInjuryService
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

    private sealed class FakeSleeperLeagueClient : ISleeperLeagueClient
    {
        public SleeperLeagueSnapshot? LeagueSnapshot { get; set; }
        public IReadOnlyList<SleeperDraftSummary> Drafts { get; set; } = [];
        public SleeperDraftSnapshot? Draft { get; set; }
        public IReadOnlyList<SleeperDraftPickSnapshot> Picks { get; set; } = [];
        public bool ThrowOnListDrafts { get; set; }
        public bool ThrowOnDraftFetch { get; set; }

        public Task<SleeperLeagueSnapshot?> GetLeagueSnapshotAsync(
            string leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(LeagueSnapshot);

        public Task<IReadOnlyList<SleeperDraftSummary>> GetDraftsForLeagueAsync(
            string leagueId, CancellationToken cancellationToken = default)
        {
            if (ThrowOnListDrafts)
            {
                throw new HttpRequestException("Simulated Sleeper outage.");
            }

            return Task.FromResult(Drafts);
        }

        public Task<SleeperDraftSnapshot?> GetDraftAsync(
            string draftId, CancellationToken cancellationToken = default)
        {
            if (ThrowOnDraftFetch)
            {
                throw new HttpRequestException("Simulated Sleeper outage.");
            }

            return Task.FromResult(Draft);
        }

        public Task<IReadOnlyList<SleeperDraftPickSnapshot>> GetDraftPicksAsync(
            string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Picks);
    }
}
