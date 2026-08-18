using Playbook.Application.Draft;
using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Historical;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Draft;
using Playbook.Core.Historical;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Infrastructure.Draft;
using Playbook.Infrastructure.Historical;
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
        // Nobody is on the clock before a draft begins, even though slot 1 resolves to a roster.
        Assert.False(report.IsOnTheClock);
        // Behaviour change (validated against a real Sleeper mock): a pre-draft board still
        // produces recommendations so the user can prepare before pick 1.01. Withholding them
        // made the assistant useless in exactly the pre-draft window it is most used.
        Assert.Contains("has not started", report.StatusMessage, StringComparison.OrdinalIgnoreCase);
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

        Assert.Contains("has not started", report.StatusMessage, StringComparison.OrdinalIgnoreCase);
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

    // ---------------------------------------------------------------- CountPositionSlots (superflex)

    [Fact]
    public void CountPositionSlots_SuperFlex_CountsAsAQBSlot_NotAsAnRbWrTeFlex()
    {
        var rosterPositions = new[] { "QB", "RB", "WR", "TE", "FLEX", "SUPER_FLEX" };

        // Direct QB + the superflex slot -> a superflex league needs ~2 startable QBs.
        Assert.Equal(2, DraftAssistantService.CountPositionSlots(rosterPositions, Position.QB));
        // Only the plain FLEX contributes to RB (0.5 share, rounds to 1 on top of the direct RB slot).
        Assert.Equal(2, DraftAssistantService.CountPositionSlots(rosterPositions, Position.RB));
    }

    [Fact]
    public void CountPositionSlots_MultipleSuperFlexSlots_EachCountsTowardQb()
    {
        var rosterPositions = new[] { "QB", "SUPER_FLEX", "SUPER_FLEX" };

        Assert.Equal(3, DraftAssistantService.CountPositionSlots(rosterPositions, Position.QB));
    }

    // ---------------------------------------------------------------- league-context variance
    // (Part XXII's "most important test": the SAME player pool must produce DIFFERENT
    // replacement values / rankings when real league settings differ.)

    [Fact]
    public void ComputeReplacementLevels_SamePlayerPool_MoreTeams_MeansADeeperReplacementBar()
    {
        var players = MakeRankedPlayers(Position.WR, count: 30);
        var projections = MakeDescendingProjections(players, startingAt: 30m);
        var smallLeague = MakeLeague(numberOfTeams: 8, rosterPositions: ["WR", "WR"]);
        var bigLeague = MakeLeague(numberOfTeams: 14, rosterPositions: ["WR", "WR"]);

        var smallLevel = DraftAssistantService.ComputeReplacementLevels(players, projections, smallLeague);
        var bigLevel = DraftAssistantService.ComputeReplacementLevels(players, projections, bigLeague);

        Assert.NotEqual(smallLevel[Position.WR], bigLevel[Position.WR]);
        Assert.True(bigLevel[Position.WR] < smallLevel[Position.WR],
            "a 14-team league drafts deeper into the position, so its replacement level must be lower");
    }

    [Fact]
    public void ComputeReplacementLevels_SamePlayerPool_MoreStartingWrSlots_MeansADeeperReplacementBar()
    {
        var players = MakeRankedPlayers(Position.WR, count: 40);
        var projections = MakeDescendingProjections(players, startingAt: 40m);
        var twoWrLeague = MakeLeague(numberOfTeams: 12, rosterPositions: ["WR", "WR"]);
        var threeWrLeague = MakeLeague(numberOfTeams: 12, rosterPositions: ["WR", "WR", "WR"]);

        var twoWrLevel = DraftAssistantService.ComputeReplacementLevels(players, projections, twoWrLeague);
        var threeWrLevel = DraftAssistantService.ComputeReplacementLevels(players, projections, threeWrLeague);

        Assert.True(threeWrLevel[Position.WR] < twoWrLevel[Position.WR],
            "a 3-WR league needs a deeper starting group, so a WR is worth more relative to replacement");
    }

    [Fact]
    public async Task GetReportAsync_AttachedDraft_Prices_Players_Using_The_Drafts_Own_Scoring_Format()
    {
        // No connected league at all — following a Sleeper mock directly (the primary real-world
        // case this milestone fixed). Ambient default would be PPR; the draft itself is Standard.
        var candidate = MakeSleeperMappedPlayer(Position.WR, "Format Sensitive WR", "s-wr");
        var pprProjection = MakeProjection(candidate.Id, points: 15m, confidence: 60);
        var standardProjection = MakeProjection(candidate.Id, points: 10m, confidence: 60);

        var projectionService = new FakeProjectionService(
            new Dictionary<Guid, PlayerProjection> { [candidate.Id] = pprProjection },
            context => context.ScoringType == ScoringType.Standard
                ? new Dictionary<Guid, PlayerProjection> { [candidate.Id] = standardProjection }
                : new Dictionary<Guid, PlayerProjection> { [candidate.Id] = pprProjection });

        var draft = new SleeperDraftSnapshot
        {
            DraftId = "1394906274017079296",
            LeagueId = "some-other-league",
            Season = "2026",
            Status = "drafting",
            Type = "snake",
            Rounds = 3,
            Teams = 8,
            DraftOrderByUserId = new Dictionary<string, int>(),
            SlotToRosterId = new Dictionary<int, int>(),
            RosterPositions = ["QB", "RB", "WR", "TE"],
            ScoringType = "std",
            LeagueTypeRaw = "0",
            Name = "Mock draft"
        };

        var sleeper = new FakeSleeperLeagueClient
        {
            Drafts = [],
            Draft = draft,
            Picks = [],
            LeagueSnapshot = null
        };

        var leagueState = new FakeLeagueState(null, null);
        var playerService = new FakePlayerService([candidate]);
        var injuryService = new FakePlayerInjuryService(new Dictionary<Guid, PlayerInjuryRecord>());
        var service = new DraftAssistantService(
            leagueState, sleeper, playerService, projectionService, injuryService,
            new FakeByeWeekProvider(), NullLogger<DraftAssistantService>.Instance);

        Assert.True(service.AttachDraft(draft.DraftId));

        var report = await service.GetReportAsync();

        Assert.NotNull(report.Recommended);
        Assert.Equal(10m, report.Recommended!.ProjectedPoints);
    }

    // ---------------------------------------------------------------- recommendation diversity

    [Fact]
    public async Task GetReportAsync_Surfaces_Distinct_Strategic_Categories_Not_Five_Copies_Of_The_Same_Pick()
    {
        var league = MakeLeague(numberOfTeams: 2, rosterPositions: ["QB", "RB", "WR", "TE"]);
        var team = MakeTeam(100);

        var rb1 = MakePlayer(Position.RB, "RB Elite"); // dominant raw fit -> Best overall
        var rb2 = MakePlayer(Position.RB, "RB Replacement");
        var te1 = MakePlayer(Position.TE, "TE Scarce"); // biggest VOR -> Best value
        var te2 = MakePlayer(Position.TE, "TE Replacement");
        var wr1 = MakePlayer(Position.WR, "WR Boom"); // highest ceiling -> Best upside
        var wr2 = MakePlayer(Position.WR, "WR Replacement");
        var qb1 = MakePlayer(Position.QB, "QB Steady"); // highest floor -> Safest floor
        var qb2 = MakePlayer(Position.QB, "QB Replacement");

        var players = new List<Player> { rb1, rb2, te1, te2, wr1, wr2, qb1, qb2 };
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [rb1.Id] = MakeProjectionWithRange(rb1.Id, points: 25m, floor: 20m, ceiling: 30m, confidence: 70),
            [rb2.Id] = MakeProjectionWithRange(rb2.Id, points: 8m, floor: 6m, ceiling: 15m, confidence: 50),
            [te1.Id] = MakeProjectionWithRange(te1.Id, points: 15m, floor: 5m, ceiling: 20m, confidence: 55),
            [te2.Id] = MakeProjectionWithRange(te2.Id, points: 4m, floor: 2m, ceiling: 8m, confidence: 40),
            [wr1.Id] = MakeProjectionWithRange(wr1.Id, points: 14m, floor: 8m, ceiling: 26m, confidence: 50),
            [wr2.Id] = MakeProjectionWithRange(wr2.Id, points: 13m, floor: 9m, ceiling: 15m, confidence: 50),
            [qb1.Id] = MakeProjectionWithRange(qb1.Id, points: 13m, floor: 12m, ceiling: 14m, confidence: 60),
            [qb2.Id] = MakeProjectionWithRange(qb2.Id, points: 12m, floor: 6m, ceiling: 13m, confidence: 55)
        };

        var sleeper = new FakeSleeperLeagueClient
        {
            Drafts = [new SleeperDraftSummary { DraftId = "d1", Status = "drafting", Season = "2026" }],
            Draft = MakeDraftSnapshot(
                new Dictionary<string, int> { ["user-me"] = 1, ["user-rival"] = 2 },
                status: "drafting", rounds: 4, teams: 2),
            Picks = [],
            LeagueSnapshot = MakeLeagueSnapshot(MakeRoster(100, "user-me"), MakeRoster(200, "user-rival"))
        };

        var service = CreateService(league, team, sleeper, players, projections);

        var report = await service.GetReportAsync();

        Assert.NotNull(report.Recommended);
        Assert.Equal(RecommendationCategory.BestOverall, report.Recommended!.Category);
        Assert.Equal(rb1.Id, report.Recommended.PlayerId);

        var categorized = new[] { report.Recommended }.Concat(report.Alternatives)
            .Where(r => r!.Category != RecommendationCategory.None)
            .ToList();

        // Every categorized recommendation must be a genuinely different player.
        Assert.Equal(categorized.Count, categorized.Select(r => r!.PlayerId).Distinct().Count());

        var byCategory = categorized.ToDictionary(r => r!.Category, r => r!.PlayerId);
        Assert.Equal(rb1.Id, byCategory[RecommendationCategory.BestOverall]);
        Assert.Equal(te1.Id, byCategory[RecommendationCategory.BestValue]);
        Assert.Equal(wr1.Id, byCategory[RecommendationCategory.BestUpside]);
        Assert.Equal(qb1.Id, byCategory[RecommendationCategory.SafestFloor]);

        // Every categorized card must carry a plain-language reason, not a bare label.
        Assert.All(categorized, r => Assert.False(string.IsNullOrWhiteSpace(r!.CategoryRationale)));
    }

    // ---------------------------------------------------------------- continuous updates (Part XIX)

    [Fact]
    public async Task GetReportAsync_Still_Computes_A_Recommendation_When_It_Is_Not_The_Users_Turn()
    {
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: false);
        var candidate = MakePlayer(Position.RB, "Any RB");
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [candidate.Id] = MakeProjection(candidate.Id, points: 12m, confidence: 50)
        };
        var service = CreateService(league, team, sleeper, [candidate], projections);

        var report = await service.GetReportAsync();

        Assert.False(report.IsOnTheClock);
        Assert.NotNull(report.Recommended);
        Assert.Equal(candidate.Id, report.Recommended!.PlayerId);
    }

    // ---------------------------------------------------------------- route tree

    [Fact]
    public async Task GetReportAsync_RouteTree_Is_Populated_When_On_The_Clock()
    {
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        var candidate = MakePlayer(Position.RB, "Any RB");
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [candidate.Id] = MakeProjection(candidate.Id, points: 12m, confidence: 50)
        };
        var service = CreateService(league, team, sleeper, [candidate], projections);

        var report = await service.GetReportAsync();

        Assert.True(report.IsOnTheClock);
        Assert.NotNull(report.RouteTree);
        Assert.Equal(candidate.Id, report.RouteTree!.BestCurrentMove!.PlayerId);
    }

    [Fact]
    public async Task GetReportAsync_RouteTree_Keeps_Updating_When_It_Is_Not_The_Users_Turn()
    {
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: false);
        var candidate = MakePlayer(Position.RB, "Any RB");
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [candidate.Id] = MakeProjection(candidate.Id, points: 12m, confidence: 50)
        };
        var service = CreateService(league, team, sleeper, [candidate], projections);

        var report = await service.GetReportAsync();

        Assert.False(report.IsOnTheClock);
        Assert.NotNull(report.RouteTree);
        Assert.Equal(candidate.Id, report.RouteTree!.BestCurrentMove!.PlayerId);
    }

    [Fact]
    public async Task GetReportAsync_Loads_Only_The_Selected_League_And_Team_Personal_Knowledge()
    {
        var preferred = MakePlayer(Position.RB, "Preferred RB");
        var other = MakePlayer(Position.RB, "Other RB");
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        var historical = HistoricalWith(
            PersonalKnowledge("lg1", "100", preferred, other, observations: 8),
            PersonalKnowledge("lg1", "200", other, preferred, observations: 12),
            PersonalKnowledge("lg2", "100", other, preferred, observations: 12));

        var service = CreateService(
            league, team, sleeper,
            [preferred, other],
            new Dictionary<Guid, PlayerProjection>
            {
                [preferred.Id] = MakeProjection(preferred.Id, 14.5m, 50),
                [other.Id] = MakeProjection(other.Id, 15.0m, 50)
            },
            historical: historical);

        var report = await service.GetReportAsync();

        Assert.Equal("lg1", report.PersonalKnowledge!.LeagueId);
        Assert.Equal("100", report.PersonalKnowledge.TeamId);
        Assert.Equal(preferred.Id, report.Recommended!.PlayerId);
        Assert.Contains(report.Recommended.Factors, f => f.Label == "Personal history");
        Assert.Contains(
            PersonalDraftLearningPolicy.HistorySentence(preferred.FullName, other.FullName, 8),
            report.Recommended.Reasoning);
        Assert.Contains("Personal history:", report.DecisionSummary);
        Assert.Equal(preferred.Id, report.RouteTree!.BestCurrentMove!.PlayerId);
    }

    [Fact]
    public async Task GetReportAsync_Does_Not_Apply_Another_Team_Or_League_Personal_Knowledge()
    {
        var preferred = MakePlayer(Position.RB, "Preferred RB");
        var other = MakePlayer(Position.RB, "Other RB");
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        var historical = HistoricalWith(
            PersonalKnowledge("lg1", "200", preferred, other, observations: 12),
            PersonalKnowledge("lg2", "100", preferred, other, observations: 12));

        var service = CreateService(
            league, team, sleeper,
            [preferred, other],
            new Dictionary<Guid, PlayerProjection>
            {
                [preferred.Id] = MakeProjection(preferred.Id, 14.0m, 50),
                [other.Id] = MakeProjection(other.Id, 16.0m, 50)
            },
            historical: historical);

        var report = await service.GetReportAsync();

        Assert.Null(report.PersonalKnowledge);
        Assert.Equal(other.Id, report.Recommended!.PlayerId);
        Assert.DoesNotContain(report.Recommended.Factors, f => f.Label == "Personal history");
        Assert.DoesNotContain("Personal history:", report.DecisionSummary);
    }

    [Fact]
    public async Task GetReportAsync_Weak_Personal_Evidence_Cannot_Overwhelm_Objective_Value()
    {
        var underdog = MakePlayer(Position.RB, "Underdog");
        var elite = MakePlayer(Position.RB, "Elite");
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        var historical = HistoricalWith(PersonalKnowledge("lg1", "100", underdog, elite, observations: 1));

        var service = CreateService(
            league, team, sleeper,
            [underdog, elite],
            new Dictionary<Guid, PlayerProjection>
            {
                [underdog.Id] = MakeProjection(underdog.Id, 10m, 50),
                [elite.Id] = MakeProjection(elite.Id, 20m, 50)
            },
            historical: historical);

        var report = await service.GetReportAsync();

        Assert.Equal(elite.Id, report.Recommended!.PlayerId);
        Assert.Equal(elite.Id, report.RouteTree!.BestCurrentMove!.PlayerId);
    }

    [Fact]
    public async Task One_Imported_Preference_Moves_A_Close_Call_Even_When_Roster_Depth_Differs()
    {
        var playerA = MakePlayer(Position.RB, "Player A");
        var playerB = MakePlayer(Position.RB, "Player B");
        var wrOwned = MakeSleeperMappedPlayer(Position.WR, "Already Drafted WR", "wr-owned");
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        sleeper.Picks =
        [
            Pick(1, 1, 1, 100, "wr-owned", "Already Drafted WR", "WR"),
            Pick(2, 1, 2, 200, "rb-rival", "Rival RB", "RB")
        ];

        var report = await CreateService(
            league, team, sleeper,
            [playerA, playerB, wrOwned],
            new Dictionary<Guid, PlayerProjection>
            {
                [playerA.Id] = MakeProjection(playerA.Id, 14.9m, 50),
                [playerB.Id] = MakeProjection(playerB.Id, 15.1m, 50)
            },
            historical: HistoricalWith(PersonalKnowledge("lg1", "100", playerA, playerB, observations: 1)))
            .GetReportAsync();

        Assert.Equal(1, report.RosterNeeds.Single(n => n.PositionLabel == "WR").CurrentCount);
        Assert.Equal(playerA.Id, report.Recommended!.PlayerId);
        Assert.Contains(report.Recommended.Factors, f => f.Label == "Personal history");
        Assert.Equal(playerA.Id, report.RouteTree!.BestCurrentMove!.PlayerId);
        Assert.Contains("Personal history:", report.DecisionSummary);
    }

    [Fact]
    public async Task ScenarioA_Repeated_Preference_Moves_A_Above_B_When_Both_Are_Available()
    {
        var playerA = MakePlayer(Position.RB, "Player A");
        var playerB = MakePlayer(Position.RB, "Player B");
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        var service = CreateService(
            league, team, sleeper,
            [playerA, playerB],
            new Dictionary<Guid, PlayerProjection>
            {
                [playerA.Id] = MakeProjection(playerA.Id, 14.6m, 50),
                [playerB.Id] = MakeProjection(playerB.Id, 15.0m, 50)
            },
            historical: HistoricalWith(PersonalKnowledge("lg1", "100", playerA, playerB, observations: 8)));

        var report = await service.GetReportAsync();

        Assert.Equal(playerA.Id, report.Recommended!.PlayerId);
        Assert.Equal(
            PersonalDraftLearningPolicy.HistorySentence(playerA.FullName, playerB.FullName, 8),
            report.Recommended.Factors.Single(f => f.Label == "Personal history").Detail);
        Assert.Contains("You selected Player A over Player B in 8 similar decisions.", report.DecisionSummary);
    }

    [Fact]
    public async Task ScenarioB_One_Weak_Decision_Does_Not_Overwhelm_A_Materially_Better_B()
    {
        var playerA = MakePlayer(Position.RB, "Player A");
        var playerB = MakePlayer(Position.RB, "Player B");
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        var service = CreateService(
            league, team, sleeper,
            [playerA, playerB],
            new Dictionary<Guid, PlayerProjection>
            {
                [playerA.Id] = MakeProjection(playerA.Id, 12m, 50),
                [playerB.Id] = MakeProjection(playerB.Id, 16m, 50)
            },
            historical: HistoricalWith(PersonalKnowledge("lg1", "100", playerA, playerB, observations: 1)));

        var report = await service.GetReportAsync();

        Assert.Equal(playerB.Id, report.Recommended!.PlayerId);
        Assert.Equal(playerB.Id, report.RouteTree!.BestCurrentMove!.PlayerId);
    }

    [Fact]
    public async Task ScenarioC_Roster_Context_Determines_Which_Preference_Applies()
    {
        var playerA = MakePlayer(Position.WR, "Player A");
        var playerB = MakePlayer(Position.WR, "Player B");
        var knowledge = PersonalKnowledgeFrom("lg1", "100",
            Pref(playerA, playerB, wr: 0, observations: 4),
            Pref(playerB, playerA, wr: 3, observations: 4));

        var (league, emptyBoard, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        var lowWr = await CreateService(
            league, team, emptyBoard,
            [playerA, playerB],
            new Dictionary<Guid, PlayerProjection>
            {
                [playerA.Id] = MakeProjection(playerA.Id, 15.0m, 50),
                [playerB.Id] = MakeProjection(playerB.Id, 15.1m, 50)
            },
            historical: HistoricalWith(knowledge)).GetReportAsync();

        Assert.Equal(playerA.Id, lowWr.Recommended!.PlayerId);
        Assert.Contains("You selected Player A over Player B", lowWr.Recommended.Reasoning);

        var wr1 = MakeSleeperMappedPlayer(Position.WR, "WR Depth 1", "wr-d1");
        var wr2 = MakeSleeperMappedPlayer(Position.WR, "WR Depth 2", "wr-d2");
        var wr3 = MakeSleeperMappedPlayer(Position.WR, "WR Depth 3", "wr-d3");
        var (highLeague, highBoard, highTeam) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        highBoard.Picks =
        [
            Pick(1, 1, 1, 100, "wr-d1", "WR Depth 1", "WR"),
            Pick(2, 1, 2, 200, "rb-r1", "Rival RB 1", "RB"),
            Pick(3, 2, 2, 200, "rb-r2", "Rival RB 2", "RB"),
            Pick(4, 2, 1, 100, "wr-d2", "WR Depth 2", "WR"),
            Pick(5, 3, 1, 100, "wr-d3", "WR Depth 3", "WR")
        ];
        var highWr = await CreateService(
            highLeague, highTeam, highBoard,
            [playerA, playerB, wr1, wr2, wr3],
            new Dictionary<Guid, PlayerProjection>
            {
                [playerA.Id] = MakeProjection(playerA.Id, 15.0m, 50),
                [playerB.Id] = MakeProjection(playerB.Id, 15.1m, 50)
            },
            historical: HistoricalWith(knowledge)).GetReportAsync();

        Assert.Equal(3, highWr.RosterNeeds.Single(n => n.PositionLabel == "WR").CurrentCount);
        Assert.Equal(playerB.Id, highWr.Recommended!.PlayerId);
        Assert.Contains("You selected Player B over Player A", highWr.Recommended.Reasoning);
    }

    [Fact]
    public async Task ScenarioD_LeagueA_TeamA_Knowledge_Does_Not_Affect_LeagueB_TeamB()
    {
        var playerA = MakePlayer(Position.RB, "Player A");
        var playerB = MakePlayer(Position.RB, "Player B");
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        var service = CreateService(
            league, team, sleeper,
            [playerA, playerB],
            new Dictionary<Guid, PlayerProjection>
            {
                [playerA.Id] = MakeProjection(playerA.Id, 14.0m, 50),
                [playerB.Id] = MakeProjection(playerB.Id, 15.5m, 50)
            },
            historical: HistoricalWith(
                PersonalKnowledge("lg-other", "100", playerA, playerB, observations: 12),
                PersonalKnowledge("lg1", "999", playerA, playerB, observations: 12)));

        var report = await service.GetReportAsync();

        Assert.Null(report.PersonalKnowledge);
        Assert.Equal(playerB.Id, report.Recommended!.PlayerId);
        Assert.DoesNotContain("Personal history:", report.DecisionSummary);
    }

    [Fact]
    public async Task ScenarioE_No_Personal_Knowledge_Leaves_Existing_Recommendations_Unchanged()
    {
        var playerA = MakePlayer(Position.RB, "Player A");
        var playerB = MakePlayer(Position.RB, "Player B");
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [playerA.Id] = MakeProjection(playerA.Id, 12m, 50),
            [playerB.Id] = MakeProjection(playerB.Id, 18m, 50)
        };

        var baseline = await CreateService(league, team, sleeper, [playerA, playerB], projections)
            .GetReportAsync();
        var withEmptyStore = await CreateService(
            league, team, sleeper, [playerA, playerB], projections,
            historical: HistoricalWith()).GetReportAsync();

        Assert.Equal(playerB.Id, baseline.Recommended!.PlayerId);
        Assert.Equal(baseline.Recommended.PlayerId, withEmptyStore.Recommended!.PlayerId);
        Assert.Equal(baseline.Recommended.TeamFitRank, withEmptyStore.Recommended.TeamFitRank);
        Assert.DoesNotContain("Personal history:", baseline.DecisionSummary);
        Assert.DoesNotContain("Personal history:", withEmptyStore.DecisionSummary);
        Assert.DoesNotContain(baseline.Recommended.Factors, f => f.Label == "Personal history");
        Assert.DoesNotContain(withEmptyStore.Recommended.Factors, f => f.Label == "Personal history");
    }

    [Fact]
    public async Task ScenarioF_Route_Tree_Incorporates_The_Learned_Preference()
    {
        var playerA = MakePlayer(Position.RB, "Player A");
        var playerB = MakePlayer(Position.RB, "Player B");
        var playerC = MakePlayer(Position.WR, "Player C");
        var (league, sleeper, team) = BuildOnTheClockScenario(nextPickGoesToUser: true);
        var report = await CreateService(
            league, team, sleeper,
            [playerA, playerB, playerC],
            new Dictionary<Guid, PlayerProjection>
            {
                [playerA.Id] = MakeProjection(playerA.Id, 14.6m, 50),
                [playerB.Id] = MakeProjection(playerB.Id, 15.0m, 50),
                [playerC.Id] = MakeProjection(playerC.Id, 11.0m, 50)
            },
            historical: HistoricalWith(PersonalKnowledge("lg1", "100", playerA, playerB, observations: 8)))
            .GetReportAsync();

        Assert.NotNull(report.RouteTree);
        Assert.Equal(playerA.Id, report.RouteTree!.BestCurrentMove!.PlayerId);
        Assert.Contains("Personal history:", report.RouteTree.BestCurrentMove.Reasoning);
        Assert.DoesNotContain(report.RouteTree.Alternatives, a => a.PlayerId == playerA.Id);
        var ifATaken = Assert.Single(report.RouteTree.IfTakenBranches, b => b.TriggerPlayerId == playerA.Id);
        Assert.NotEqual(playerA.Id, ifATaken.ThenRecommend.PlayerId);
        Assert.Equal(playerB.Id, ifATaken.ThenRecommend.PlayerId);
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
        IReadOnlyDictionary<Guid, PlayerInjuryRecord>? injuries = null,
        IHistoricalLeagueIntelligenceService? historical = null)
    {
        var leagueState = new FakeLeagueState(league, team);
        var playerService = new FakePlayerService(players ?? []);
        var projectionService = new FakeProjectionService(projections ?? new Dictionary<Guid, PlayerProjection>());
        var injuryService = new FakePlayerInjuryService(injuries ?? new Dictionary<Guid, PlayerInjuryRecord>());

        return new DraftAssistantService(
            leagueState, sleeper, playerService, projectionService, injuryService,
            new FakeByeWeekProvider(),
            NullLogger<DraftAssistantService>.Instance,
            historical);
    }

    private static IHistoricalLeagueIntelligenceService HistoricalWith(params PersonalDraftKnowledge[] knowledge)
    {
        var hist = new HistoricalLeagueDraftStore(
            NullLogger<HistoricalLeagueDraftStore>.Instance, $"da-hist-{Guid.NewGuid():N}.json");
        var personal = new PersonalDraftKnowledgeStore(
            NullLogger<PersonalDraftKnowledgeStore>.Instance, $"da-personal-{Guid.NewGuid():N}.json");
        personal.Save(knowledge);
        return new HistoricalLeagueIntelligenceService(hist, new NullSleeperForHistory(), new PlayerIdentityDirectory(), personal);
    }

    private static PersonalDraftKnowledge PersonalKnowledge(
        string leagueId, string teamId, Player preferred, Player passed, int observations,
        IReadOnlyDictionary<string, int>? rosterBefore = null) =>
        PersonalKnowledgeFrom(leagueId, teamId, Pref(preferred, passed, wr: rosterBefore is not null && rosterBefore.TryGetValue("WR", out var wr) ? wr : 0, observations));

    private static PersonalDraftKnowledge PersonalKnowledgeFrom(
        string leagueId, string teamId, params PersonalPlayerPreference[] preferences) => new()
    {
        LeagueId = leagueId,
        TeamId = teamId,
        LeagueName = "Boys League",
        TeamName = "My Team",
        DraftCount = Math.Max(1, preferences.Length),
        DecisionCount = preferences.Sum(p => p.ObservationCount),
        Preferences = preferences
    };

    private static PersonalPlayerPreference Pref(Player preferred, Player passed, int wr, int observations) =>
        new(
            preferred.Id.ToString("N"),
            preferred.FullName,
            passed.Id.ToString("N"),
            passed.FullName,
            new PersonalPreferenceContext(
                LeagueType.Redraft,
                "PPR",
                2,
                1,
                1,
                wr <= 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["WR"] = wr },
                [passed.Id.ToString("N")]),
            observations,
            ["seed"]);

    private static SleeperDraftPickSnapshot Pick(
        int pickNumber, int round, int slot, int rosterId, string sleeperId, string name, string position) => new()
    {
        PickNumber = pickNumber,
        Round = round,
        DraftSlot = slot,
        RosterId = rosterId,
        SleeperPlayerId = sleeperId,
        PlayerName = name,
        Position = position
    };

    private sealed class NullSleeperForHistory : ISleeperLeagueClient
    {
        public Task<SleeperLeagueSnapshot?> GetLeagueSnapshotAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SleeperLeagueSnapshot?>(null);
        public Task<IReadOnlyList<SleeperDraftSummary>> GetDraftsForLeagueAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SleeperDraftSummary>>([]);
        public Task<SleeperDraftSnapshot?> GetDraftAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SleeperDraftSnapshot?>(null);
        public Task<IReadOnlyList<SleeperDraftPickSnapshot>> GetDraftPicksAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SleeperDraftPickSnapshot>>([]);
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

    private static PlayerProjection MakeProjectionWithRange(
        Guid playerId, decimal points, decimal floor, decimal ceiling, int confidence) => new()
    {
        PlayerId = playerId,
        Week = 1,
        ScoringFormat = ScoringType.Ppr,
        ProjectedFantasyPoints = points,
        Floor = floor,
        Median = points,
        Ceiling = ceiling,
        Confidence = confidence,
        Volatility = 30,
        ProjectionReasoning = [],
        SupportingIntelligence = [],
        ProjectionTimestamp = DateTimeOffset.UtcNow,
        ProjectionVersion = "test",
        InputsUsed = new ProjectionInputsUsed()
    };

    /// <summary>N undrafted players at one position, for tests that need a real depth chart rather
    /// than a handful of hand-picked candidates (league-size / roster-shape variance tests).</summary>
    private static List<Player> MakeRankedPlayers(Position position, int count) =>
        Enumerable.Range(1, count).Select(i => MakePlayer(position, $"{position} #{i}")).ToList();

    private static Dictionary<Guid, PlayerProjection> MakeDescendingProjections(
        IReadOnlyList<Player> rankedPlayers, decimal startingAt) =>
        rankedPlayers
            .Select((p, i) => (Player: p, Points: startingAt - i))
            .ToDictionary(x => x.Player.Id, x => MakeProjection(x.Player.Id, points: x.Points, confidence: 50));

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

        /// <summary>
        /// Optional per-context override, so tests can prove the Draft Assistant actually feeds
        /// an attached draft's own scoring context into projections rather than always reusing
        /// the ambient league. When absent, every context falls back to <see cref="_projections"/>.
        /// </summary>
        private readonly Func<ProjectionLeagueContext, IReadOnlyDictionary<Guid, PlayerProjection>>? _byContext;

        public FakeProjectionService(IReadOnlyDictionary<Guid, PlayerProjection> projections) =>
            _projections = projections;

        public FakeProjectionService(
            IReadOnlyDictionary<Guid, PlayerProjection> projections,
            Func<ProjectionLeagueContext, IReadOnlyDictionary<Guid, PlayerProjection>> byContext)
        {
            _projections = projections;
            _byContext = byContext;
        }

        public string EngineVersion => "test";
        public PlayerProjection? GetProjection(Guid playerId) => _projections.GetValueOrDefault(playerId);
        public PlayerProjection? ProjectPlayer(Guid playerId) => GetProjection(playerId);
        public IReadOnlyList<PlayerProjection> GetAllProjections() => _projections.Values.ToList();
        public IReadOnlyList<PlayerProjection> GetAllProjections(ProjectionLeagueContext context) =>
            (_byContext?.Invoke(context) ?? _projections).Values.ToList();
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
