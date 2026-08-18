using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Draft;
using Playbook.Application.Injuries;
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
using Playbook.Infrastructure.Leagues;
using Playbook.Infrastructure.Players;

namespace Playbook.Tests;

/// <summary>
/// Validation against the real completed Boys League mock
/// sleeper.app/draft/nfl/1395528272707612672 (league_mock for 1389392411389628416).
/// Frozen pick/owner facts — not a live Sleeper call, not a fabricated ranking.
/// </summary>
public class PersonalLearningValidationTests : IDisposable
{
    internal const string MockDraftId = "1395528272707612672";
    internal const string BoysLeagueId = "1389392411389628416";
    internal const string NeuhaUserId = "990814674046287872";
    internal const string PlayboicoriUserId = "1138414973539139584";
    internal const int NeuhaRosterId = 3;
    internal const int PlayboicoriRosterId = 8;

    private readonly List<string> _files = [];

    [Fact]
    public async Task Sleeper_Client_Uses_Metadata_League_Id_When_Top_Level_League_Id_Is_Omitted()
    {
        var json = """
            {"draft_id":"1395528272707612672","league_id":null,"season":"2026","status":"complete","type":"snake",
             "settings":{"rounds":16,"teams":10},
             "draft_order":{"990814674046287872":8},
             "slot_to_roster_id":{"1":1,"8":8},
             "metadata":{"league_id":"1389392411389628416","name":"Boys League","scoring_type":"ppr","type":"league_mock"}}
            """;
        var http = new HttpClient(new StaticJsonHandler("/v1/draft/1395528272707612672", json))
        {
            BaseAddress = new Uri("https://api.sleeper.app/v1/")
        };
        var client = new SleeperLeagueClient(new FixedFactory(http), NullLogger<SleeperLeagueClient>.Instance);

        var draft = await client.GetDraftAsync(MockDraftId);

        Assert.NotNull(draft);
        Assert.Equal(BoysLeagueId, draft!.LeagueId);
        Assert.Equal("ppr", draft.ScoringType);
        Assert.Equal(NeuhaUserId, Assert.Single(draft.DraftOrderByUserId).Key);
    }

    [Fact]
    public async Task Real_Boys_League_Mock_Creates_Personal_PvP_Decisions_For_Neuha()
    {
        var (_, personal, service) = NewImport(withSourceLeague: true);
        var before = service.GetPersonalKnowledge(BoysLeagueId, NeuhaRosterId.ToString());
        Assert.Null(before);

        var result = await service.ImportSleeperDraftForPersonalLearningAsync(MockDraftId, NeuhaScope());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.NotNull(result.PersonalKnowledge);
        Assert.Equal(BoysLeagueId, result.PersonalKnowledge!.LeagueId);
        Assert.Equal("3", result.PersonalKnowledge.TeamId);
        Assert.Equal(1, result.PersonalKnowledge.DraftCount);
        Assert.True(result.PersonalKnowledge.DecisionCount > 0);
        Assert.Contains(result.PersonalKnowledge.Preferences, p =>
            p.PreferredPlayerName == "James Cook" && p.PassedPlayerName == "Amon-Ra St. Brown");
        Assert.Contains(result.PersonalKnowledge.Preferences, p =>
            p.PreferredPlayerName == "James Cook" && p.PassedPlayerName == "CeeDee Lamb");
        Assert.Contains(result.PersonalKnowledge.Preferences, p =>
            p.PreferredPlayerName == "James Cook" && p.PassedPlayerName == "De'Von Achane");
        Assert.Equal("PPR", result.PersonalKnowledge.Preferences[0].Context.ScoringFormat);
        Assert.NotNull(service.GetPersonalKnowledge(BoysLeagueId, "3"));
        var stored = personal.Load().Single();
        Assert.Equal(1, stored.DraftCount);
        Assert.Equal(result.PersonalKnowledge.DecisionCount, stored.DecisionCount);
        Assert.Equal(result.PersonalKnowledge.Preferences.Count, stored.Preferences.Count);
    }

    [Fact]
    public async Task Playboicori_Roster_Does_Not_Inherit_Neuha_Mock_Picks()
    {
        var (_, _, service) = NewImport(withSourceLeague: true);
        var result = await service.ImportSleeperDraftForPersonalLearningAsync(MockDraftId, PlayboicoriScope());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Null(result.PersonalKnowledge);
        Assert.Null(service.GetPersonalKnowledge(BoysLeagueId, PlayboicoriRosterId.ToString()));
        Assert.Contains(result.Warnings, w => w.Contains("identify this team's picks", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Fallback_Mock_Without_Source_League_Still_Learns_By_User_Id_Not_Slot_Roster()
    {
        var (_, _, service) = NewImport(withSourceLeague: false);

        var neuha = await service.ImportSleeperDraftForPersonalLearningAsync(MockDraftId, NeuhaScope());
        Assert.True(neuha.Succeeded, string.Join("; ", neuha.Errors));
        Assert.NotNull(neuha.PersonalKnowledge);
        Assert.Equal("PPR", neuha.PersonalKnowledge!.Preferences[0].Context.ScoringFormat);
        Assert.Contains(neuha.PersonalKnowledge.Preferences, p =>
            p.PreferredPlayerName == "James Cook" && p.PassedPlayerName == "Amon-Ra St. Brown");

        var other = await service.ImportSleeperDraftForPersonalLearningAsync(MockDraftId, PlayboicoriScope());
        Assert.Null(other.PersonalKnowledge);
        Assert.Null(service.GetPersonalKnowledge(BoysLeagueId, "8"));
    }

    [Fact]
    public async Task Second_Draft_For_The_Same_League_And_Team_Accumulates()
    {
        var (sleeper, _, service) = NewImport(withSourceLeague: true);
        var first = await service.ImportSleeperDraftForPersonalLearningAsync(MockDraftId, NeuhaScope());
        Assert.Equal(1, first.PersonalKnowledge!.DraftCount);
        var cookVsAmon = first.PersonalKnowledge.Preferences.Single(p =>
            p.PreferredPlayerName == "James Cook" && p.PassedPlayerName == "Amon-Ra St. Brown");
        Assert.Equal(1, cookVsAmon.ObservationCount);

        sleeper.Draft = MockDraft("1395528272707612999");
        var second = await service.ImportSleeperDraftForPersonalLearningAsync("1395528272707612999", NeuhaScope());

        Assert.Equal(2, second.PersonalKnowledge!.DraftCount);
        Assert.True(second.PersonalKnowledge.DecisionCount > first.PersonalKnowledge.DecisionCount);
        var twice = second.PersonalKnowledge.Preferences.Single(p =>
            p.PreferredPlayerName == "James Cook" && p.PassedPlayerName == "Amon-Ra St. Brown");
        Assert.Equal(2, twice.ObservationCount);
    }

    [Fact]
    public async Task Imported_Cook_Over_AmonRa_Moves_A_Close_Live_Recommendation()
    {
        var (_, _, historical) = NewImport(withSourceLeague: true);
        await historical.ImportSleeperDraftForPersonalLearningAsync(MockDraftId, NeuhaScope());

        var cook = Mapped("James Cook", Position.RB, "8138");
        var amon = Mapped("Amon-Ra St. Brown", Position.WR, "7547");
        var report = await LiveReport(historical, cook, amon, cookPts: 14.95m, amonPts: 15.05m, rosterId: NeuhaRosterId);

        Assert.Equal("3", report.PersonalKnowledge!.TeamId);
        Assert.Equal(cook.Id, report.Recommended!.PlayerId);
        Assert.Contains(report.Recommended.Factors, f => f.Label == "Personal history");
        Assert.Contains("James Cook over Amon-Ra St. Brown", report.Recommended.Reasoning);
        Assert.Equal(cook.Id, report.RouteTree!.BestCurrentMove!.PlayerId);
        Assert.Contains("Personal history:", report.RouteTree.BestCurrentMove.Reasoning);
        Assert.DoesNotContain(report.RouteTree.Alternatives, a => a.PlayerId == cook.Id);
        var ifTaken = Assert.Single(report.RouteTree.IfTakenBranches, b => b.TriggerPlayerId == cook.Id);
        Assert.Equal(amon.Id, ifTaken.ThenRecommend.PlayerId);
    }

    [Fact]
    public async Task Imported_Cook_Over_AmonRa_Is_Recognized_When_Live_Roster_Is_Only_Similar()
    {
        var (_, _, historical) = NewImport(withSourceLeague: true);
        await historical.ImportSleeperDraftForPersonalLearningAsync(MockDraftId, NeuhaScope());
        var knowledge = historical.GetPersonalKnowledge(BoysLeagueId, "3");
        Assert.NotNull(knowledge);

        var cookKey = "sleeper:8138";
        var amonKey = "sleeper:7547";
        var league = new League
        {
            Id = Guid.NewGuid(),
            Name = "Boys League",
            Platform = LeaguePlatform.Sleeper,
            LeagueType = LeagueType.Redraft,
            ScoringType = ScoringType.Ppr,
            NumberOfTeams = 10,
            CurrentWeek = 1,
            Season = 2026,
            IsActive = true,
            DataSource = LeagueDataSource.Sleeper,
            ExternalId = BoysLeagueId,
            RosterPositions = ["QB", "RB", "WR", "TE"]
        };
        var live = PersonalDraftLearningPolicy.LiveContext(
            league, 10, 2, 13,
            new Dictionary<string, int> { ["WR"] = 1, ["RB"] = 0, ["TE"] = 0, ["QB"] = 0 },
            [cookKey, amonKey]);
        var available = new List<(string Key, string Name, decimal Projection)>
        {
            (amonKey, "Amon-Ra St. Brown", 15.1m),
            (cookKey, "James Cook", 14.9m)
        };

        var forCook = PersonalDraftLearningPolicy.Adjust(
            cookKey, "James Cook", 14.9m, available, knowledge!, live);
        var forAmon = PersonalDraftLearningPolicy.Adjust(
            amonKey, "Amon-Ra St. Brown", 15.1m, available, knowledge!, live);

        Assert.True(forCook.ScoreDelta > 0);
        Assert.True(14.9m + forCook.ScoreDelta > 15.1m + forAmon.ScoreDelta);
        Assert.Contains("James Cook over Amon-Ra St. Brown", forCook.Factor!.Detail);
    }

    [Fact]
    public async Task Weak_Personal_History_Does_Not_Overturn_A_Major_Projection_Gap()
    {
        var (_, _, historical) = NewImport(withSourceLeague: true);
        await historical.ImportSleeperDraftForPersonalLearningAsync(MockDraftId, NeuhaScope());

        var cook = Mapped("James Cook", Position.RB, "8138");
        var amon = Mapped("Amon-Ra St. Brown", Position.WR, "7547");
        var report = await LiveReport(historical, cook, amon, cookPts: 10m, amonPts: 20m, rosterId: NeuhaRosterId);

        Assert.Equal(amon.Id, report.Recommended!.PlayerId);
        Assert.Equal(amon.Id, report.RouteTree!.BestCurrentMove!.PlayerId);
    }

    [Fact]
    public async Task Switching_Team_Removes_Personal_Knowledge_From_Recommendations()
    {
        var (_, _, historical) = NewImport(withSourceLeague: true);
        await historical.ImportSleeperDraftForPersonalLearningAsync(MockDraftId, NeuhaScope());

        var cook = Mapped("James Cook", Position.RB, "8138");
        var amon = Mapped("Amon-Ra St. Brown", Position.WR, "7547");
        var otherTeam = await LiveReport(
            historical, cook, amon, cookPts: 14.95m, amonPts: 15.05m, rosterId: PlayboicoriRosterId,
            ownerUserId: PlayboicoriUserId);

        Assert.Null(otherTeam.PersonalKnowledge);
        Assert.Equal(amon.Id, otherTeam.Recommended!.PlayerId);
        Assert.DoesNotContain(otherTeam.Recommended.Factors, f => f.Label == "Personal history");
        Assert.DoesNotContain("Personal history:", otherTeam.DecisionSummary);
    }

    public void Dispose()
    {
        foreach (var file in _files)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* cleanup */ }
        }
    }

    private (FakeSleeper sleeper, PersonalDraftKnowledgeStore personal, HistoricalLeagueIntelligenceService service) NewImport(
        bool withSourceLeague)
    {
        var hist = new HistoricalLeagueDraftStore(NullLogger<HistoricalLeagueDraftStore>.Instance, Temp("hist"));
        var personal = new PersonalDraftKnowledgeStore(
            NullLogger<PersonalDraftKnowledgeStore>.Instance, Temp("personal"));
        var sleeper = new FakeSleeper
        {
            Draft = MockDraft(leagueId: withSourceLeague ? BoysLeagueId : ""),
            Picks = RealOpeningPicks(),
            LeagueSnapshot = withSourceLeague ? BoysLeagueSnapshot() : null
        };
        var service = new HistoricalLeagueIntelligenceService(hist, sleeper, new PlayerIdentityDirectory(), personal);
        return (sleeper, personal, service);
    }

    private string Temp(string prefix)
    {
        var name = $"{prefix}-{Guid.NewGuid():N}.json";
        _files.Add(Path.Combine(AppContext.BaseDirectory, "data", name));
        return name;
    }

    private static PersonalDraftLearningRequest NeuhaScope() =>
        new(BoysLeagueId, "3", "Boys League", "My Chubb Fell Off", NeuhaRosterId, NeuhaUserId, "Neuha");

    private static PersonalDraftLearningRequest PlayboicoriScope() =>
        new(BoysLeagueId, "8", "Boys League", "49IRS", PlayboicoriRosterId, PlayboicoriUserId, "playboicori");

    private static SleeperDraftSnapshot MockDraft(string draftId = MockDraftId, string? leagueId = null) => new()
    {
        DraftId = draftId,
        LeagueId = leagueId ?? BoysLeagueId,
        Season = "2026",
        Status = "complete",
        Type = "snake",
        Rounds = 2,
        Teams = 10,
        DraftOrderByUserId = new Dictionary<string, int> { [NeuhaUserId] = 8 },
        SlotToRosterId = Enumerable.Range(1, 10).ToDictionary(i => i, i => i),
        RosterPositions = ["QB", "RB", "WR", "TE"],
        ScoringType = "ppr",
        LeagueTypeRaw = "0",
        Name = "Boys League"
    };

    private static SleeperLeagueSnapshot BoysLeagueSnapshot() => new()
    {
        ExternalLeagueId = BoysLeagueId,
        Name = "Boys League",
        Season = "2026",
        Status = "pre_draft",
        TeamCount = 10,
        CurrentWeek = 1,
        SleeperLeagueType = 0,
        ScoringSettings = new Dictionary<string, double> { ["rec"] = 1.0 },
        RosterPositions = ["QB", "RB", "WR", "TE"],
        Rosters =
        [
            Roster(1, "984666646356860928", "Jared23Ellis"),
            Roster(2, "1126995956462940160", "Tjc94"),
            Roster(3, NeuhaUserId, "Neuha", "My Chubb Fell Off"),
            Roster(4, "730266905222467584", "CZR"),
            Roster(5, "1127007072744353792", "SleepyJoe1414"),
            Roster(6, "1127022119990292480", "Iza55"),
            Roster(7, "1127044930406457344", "BiGDAWS99"),
            Roster(8, PlayboicoriUserId, "playboicori", "49IRS"),
            Roster(9, "1129151660305010688", "messyjessee"),
            Roster(10, "1393734622986125312", "renatoK")
        ]
    };

    private static SleeperRosterSnapshot Roster(int id, string owner, string name, string? team = null) => new()
    {
        RosterId = id,
        OwnerId = owner,
        OwnerName = name,
        TeamName = team ?? name,
        SleeperPlayerIds = [],
        StarterSleeperPlayerIds = [],
        ReserveSleeperPlayerIds = [],
        TaxiSleeperPlayerIds = []
    };

    /// <summary>
    /// First 18 picks of draft 1395528272707612672. Window after Cook (pick 8) is 10 teams,
    /// so these are the actual on-the-board alternatives for that decision.
    /// </summary>
    private static List<SleeperDraftPickSnapshot> RealOpeningPicks() =>
    [
        Cpu(1, 1, "7564", "Ja'Marr Chase", "WR"),
        Cpu(2, 2, "9221", "Jahmyr Gibbs", "RB"),
        Cpu(3, 3, "9509", "Bijan Robinson", "RB"),
        Cpu(4, 4, "9493", "Puka Nacua", "WR"),
        Cpu(5, 5, "9488", "Jaxon Smith-Njigba", "WR"),
        Cpu(6, 6, "4034", "Christian McCaffrey", "RB"),
        Cpu(7, 7, "6813", "Jonathan Taylor", "RB"),
        Human(8, 8, "8138", "James Cook", "RB"),
        Cpu(9, 9, "7547", "Amon-Ra St. Brown", "WR"),
        Cpu(10, 10, "6786", "CeeDee Lamb", "WR"),
        Cpu(11, 10, "9226", "De'Von Achane", "RB"),
        Cpu(12, 9, "12527", "Ashton Jeanty", "RB"),
        Human(13, 8, "9224", "Chase Brown", "RB"),
        Cpu(14, 7, "8112", "Drake London", "WR"),
        Cpu(15, 6, "6794", "Justin Jefferson", "WR"),
        Cpu(16, 5, "4866", "Saquon Barkley", "RB"),
        Cpu(17, 4, "12507", "Omarion Hampton", "RB"),
        Cpu(18, 3, "3198", "Derrick Henry", "RB")
    ];

    private static SleeperDraftPickSnapshot Cpu(int pick, int slot, string id, string name, string pos) =>
        new()
        {
            PickNumber = pick, Round = pick <= 10 ? 1 : 2, DraftSlot = slot, RosterId = null,
            PickedByUserId = "", SleeperPlayerId = id, PlayerName = name, Position = pos
        };

    private static SleeperDraftPickSnapshot Human(int pick, int slot, string id, string name, string pos) =>
        new()
        {
            PickNumber = pick, Round = pick <= 10 ? 1 : 2, DraftSlot = slot, RosterId = null,
            PickedByUserId = NeuhaUserId, SleeperPlayerId = id, PlayerName = name, Position = pos
        };

    private static Player Mapped(string name, Position position, string sleeperId) => new()
    {
        Id = SleeperPlayerIds.ToPlaybookId(sleeperId),
        FullName = name,
        FirstName = name,
        LastName = name,
        Position = position,
        Team = "FA",
        Status = PlayerStatus.Active
    };

    private static async Task<DraftAssistantReport> LiveReport(
        HistoricalLeagueIntelligenceService historical,
        Player cook,
        Player amon,
        decimal cookPts,
        decimal amonPts,
        int rosterId,
        string ownerUserId = NeuhaUserId)
    {
        var league = new League
        {
            Id = Guid.NewGuid(),
            Name = "Boys League",
            Platform = LeaguePlatform.Sleeper,
            LeagueType = LeagueType.Redraft,
            ScoringType = ScoringType.Ppr,
            NumberOfTeams = 10,
            CurrentWeek = 1,
            Season = 2026,
            IsActive = true,
            DataSource = LeagueDataSource.Sleeper,
            ExternalId = BoysLeagueId,
            RosterPositions = ["QB", "RB", "WR", "TE"]
        };
        var team = new FantasyTeam
        {
            LeagueId = league.Id,
            RosterId = rosterId,
            OwnerUserId = ownerUserId,
            DisplayName = rosterId == NeuhaRosterId ? "Neuha" : "playboicori",
            TeamName = rosterId == NeuhaRosterId ? "My Chubb Fell Off" : "49IRS",
            PlayerIds = [],
            StarterIds = []
        };
        var sleeper = new LiveFakeSleeper
        {
            Drafts = [new SleeperDraftSummary { DraftId = "live", Status = "drafting", Season = "2026" }],
            Draft = new SleeperDraftSnapshot
            {
                DraftId = "live",
                LeagueId = BoysLeagueId,
                Season = "2026",
                Status = "drafting",
                Type = "snake",
                Rounds = 3,
                Teams = 10,
                DraftOrderByUserId = new Dictionary<string, int> { [NeuhaUserId] = 1, [PlayboicoriUserId] = 2 }
            },
            Picks = [],
            LeagueSnapshot = BoysLeagueSnapshot()
        };
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [cook.Id] = Proj(cook.Id, cookPts),
            [amon.Id] = Proj(amon.Id, amonPts)
        };
        var service = new DraftAssistantService(
            new LiveFakeLeagueState(league, team),
            sleeper,
            new LiveFakePlayers([cook, amon]),
            new LiveFakeProjections(projections),
            new LiveFakeInjuries(),
            new LiveFakeByes(),
            NullLogger<DraftAssistantService>.Instance,
            historical);
        return await service.GetReportAsync();
    }

    private static PlayerProjection Proj(Guid id, decimal pts) => new()
    {
        PlayerId = id,
        Week = 1,
        ScoringFormat = ScoringType.Ppr,
        ProjectedFantasyPoints = pts,
        Floor = pts,
        Median = pts,
        Ceiling = pts,
        Confidence = 50,
        Volatility = 30,
        ProjectionReasoning = [],
        SupportingIntelligence = [],
        ProjectionTimestamp = DateTimeOffset.UtcNow,
        ProjectionVersion = "test",
        InputsUsed = new ProjectionInputsUsed()
    };

    private sealed class FakeSleeper : ISleeperLeagueClient
    {
        public SleeperDraftSnapshot? Draft { get; set; }
        public IReadOnlyList<SleeperDraftPickSnapshot> Picks { get; set; } = [];
        public SleeperLeagueSnapshot? LeagueSnapshot { get; set; }

        public Task<SleeperLeagueSnapshot?> GetLeagueSnapshotAsync(string leagueId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(leagueId);
            return Task.FromResult(LeagueSnapshot);
        }

        public Task<IReadOnlyList<SleeperDraftSummary>> GetDraftsForLeagueAsync(string leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SleeperDraftSummary>>([]);
        public Task<SleeperDraftSnapshot?> GetDraftAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Draft);
        public Task<IReadOnlyList<SleeperDraftPickSnapshot>> GetDraftPicksAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Picks);
    }

    private sealed class LiveFakeSleeper : ISleeperLeagueClient
    {
        public SleeperLeagueSnapshot? LeagueSnapshot { get; set; }
        public IReadOnlyList<SleeperDraftSummary> Drafts { get; set; } = [];
        public SleeperDraftSnapshot? Draft { get; set; }
        public IReadOnlyList<SleeperDraftPickSnapshot> Picks { get; set; } = [];

        public Task<SleeperLeagueSnapshot?> GetLeagueSnapshotAsync(string leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(LeagueSnapshot);
        public Task<IReadOnlyList<SleeperDraftSummary>> GetDraftsForLeagueAsync(string leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Drafts);
        public Task<SleeperDraftSnapshot?> GetDraftAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Draft);
        public Task<IReadOnlyList<SleeperDraftPickSnapshot>> GetDraftPicksAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Picks);
    }

    private sealed class LiveFakeLeagueState(League league, FantasyTeam team) : ILeagueState
    {
        public League? CurrentLeague => league;
        public FantasyTeam? CurrentUserTeam => team;
        public event Action? Changed { add { } remove { } }
        public IReadOnlyList<League> GetAllLeagues() => [league];
        public League? GetCurrentLeague() => league;
        public void SelectLeague(Guid leagueId) { }
        public IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId) => [team];
        public IReadOnlyList<FantasyTeam> GetCurrentTeams() => [team];
        public FantasyTeam? FindTeamForPlayer(Guid playerId) => null;
        public FantasyTeam? GetUserTeam(Guid leagueId) => team;
        public FantasyTeam? GetCurrentUserTeam() => team;
        public bool SelectUserTeam(Guid leagueId, int rosterId) => false;
        public Task<LeagueConnectResult> ConnectSleeperLeagueAsync(string sleeperLeagueId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class LiveFakePlayers(IReadOnlyList<Player> players) : IPlayerService
    {
        public IReadOnlyList<Player> GetAllPlayers() => players;
        public Player? GetPlayer(Guid playerId) => players.FirstOrDefault(p => p.Id == playerId);
        public PlayerProfile? GetPlayerProfile(Guid playerId) => null;
        public IReadOnlyList<Player> SearchPlayers(string? query) => players;
        public void Refresh() { }
    }

    private sealed class LiveFakeProjections(IReadOnlyDictionary<Guid, PlayerProjection> projections) : IProjectionService
    {
        public string EngineVersion => "test";
        public PlayerProjection? GetProjection(Guid playerId) => projections.GetValueOrDefault(playerId);
        public PlayerProjection? ProjectPlayer(Guid playerId) => GetProjection(playerId);
        public IReadOnlyList<PlayerProjection> GetAllProjections() => projections.Values.ToList();
        public IReadOnlyList<PlayerProjection> GetAllProjections(ProjectionLeagueContext context) => projections.Values.ToList();
        public IReadOnlyList<PlayerProjection> GetTopProjections(int count = 8) =>
            projections.Values.OrderByDescending(p => p.ProjectedFantasyPoints).Take(count).ToList();
        public PlayerProjectionComparison? ComparePlayers(Guid leftPlayerId, Guid rightPlayerId) => null;
        public IReadOnlyList<PlayerProjection> ProjectRoster(IEnumerable<Guid> playerIds) =>
            projections.Values.Where(p => playerIds.Contains(p.PlayerId)).ToList();
        public void Refresh() { }
        public void Invalidate() { }
    }

    private sealed class LiveFakeInjuries : IPlayerInjuryService
    {
        public InjuryProviderCapabilities ActiveCapabilities => InjuryProviderCapabilities.MockCurrentOnly;
        public HistoricalDataStatus GlobalHistoricalDataStatus => HistoricalDataStatus.NotSupportedByProvider;
        public IReadOnlyList<PlayerInjuryRecord> GetAllInjuries() => [];
        public IReadOnlyList<PlayerInjuryRecord> GetInjuriesForPlayer(Guid playerId) => [];
        public PlayerInjuryRecord? GetCurrentInjury(Guid playerId) => null;
        public IReadOnlyList<PlayerInjuryRecord> GetHistoricalInjuries(Guid playerId) => [];
        public PlayerInjuryProfile GetPlayerInjuryProfile(Guid playerId) => new() { PlayerId = playerId };
        public void Refresh() { }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class LiveFakeByes : IByeWeekProvider
    {
        public ByeWeekMap GetByeWeeks(int season) => ByeWeekMap.Empty;
        public Task RefreshAsync(int season, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticJsonHandler(string pathEndsWith, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.EndsWith(pathEndsWith.TrimStart('/'), StringComparison.Ordinal)
                && path != pathEndsWith)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
