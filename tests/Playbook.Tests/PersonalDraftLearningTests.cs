using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Core.Draft;
using Playbook.Core.Historical;
using Playbook.Core.Leagues;
using Playbook.Infrastructure.Historical;
using Playbook.Infrastructure.Players;

namespace Playbook.Tests;

public class PersonalDraftLearningTests : IDisposable
{
    private readonly List<string> _files = [];

    [Fact]
    public async Task Personal_Import_Requires_Selected_League_And_Team_And_Does_Not_Partially_Import()
    {
        var (store, personal, service) = NewServices();
        var sleeper = new FakeSleeper { Draft = CompletedDraft(), Picks = ThreePicks(), LeagueSnapshot = LeagueSnapshot() };
        service = NewService(store, personal, sleeper);

        var missingBoth = await service.ImportSleeperDraftForPersonalLearningAsync("123456789012", null);
        Assert.False(missingBoth.Succeeded);
        Assert.Contains(missingBoth.Errors, e => e.Contains("league", StringComparison.OrdinalIgnoreCase)
                                                && e.Contains("team", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(store.Load());
        Assert.Empty(personal.Load());

        var missingTeam = await service.ImportSleeperDraftForPersonalLearningAsync(
            "123456789012", new PersonalDraftLearningRequest("lg1", "", "Boys League", "Team"));
        Assert.False(missingTeam.Succeeded);
        Assert.Empty(store.Load());
        Assert.Empty(personal.Load());
    }

    [Fact]
    public async Task Knowledge_Is_Stored_Under_Selected_LeagueId_And_TeamId()
    {
        var (store, personal, service) = NewLiveImport();

        var result = await service.ImportSleeperDraftForPersonalLearningAsync("123456789012", Scope("lg1", "1"));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.NotNull(result.PersonalKnowledge);
        Assert.Equal("lg1", result.PersonalKnowledge!.LeagueId);
        Assert.Equal("1", result.PersonalKnowledge.TeamId);

        var loaded = service.GetPersonalKnowledge("lg1", "1");
        Assert.NotNull(loaded);
        Assert.Equal("lg1", loaded!.LeagueId);
        Assert.Equal("1", loaded.TeamId);
    }

    [Fact]
    public void Same_League_And_Team_Accumulates_Evidence_Across_Uploads()
    {
        var (_, _, service) = NewServices();
        var scope = Scope("lg1", "1");

        var first = service.LearnFromImportedDraft(ImportedDraft("d1"), scope);
        Assert.NotNull(first);
        Assert.Equal(1, first!.DraftCount);

        var second = service.LearnFromImportedDraft(ImportedDraft("d2"), scope);
        Assert.NotNull(second);
        Assert.Equal(2, second!.DraftCount);
        Assert.True(second.DecisionCount >= first.DecisionCount);
        var pair = second.Preferences.Single(p => p.PreferredPlayerName == "Chosen RB" && p.PassedPlayerName == "Passed WR");
        Assert.Equal(2, pair.ObservationCount);
    }

    [Fact]
    public async Task Different_Team_Does_Not_Inherit_Knowledge()
    {
        var (_, _, service) = NewLiveImport();
        await service.ImportSleeperDraftForPersonalLearningAsync("123456789012", Scope("lg1", "1"));

        Assert.NotNull(service.GetPersonalKnowledge("lg1", "1"));
        Assert.Null(service.GetPersonalKnowledge("lg1", "2"));
    }

    [Fact]
    public async Task Different_League_Does_Not_Inherit_Knowledge()
    {
        var (_, _, service) = NewLiveImport();
        await service.ImportSleeperDraftForPersonalLearningAsync("123456789012", Scope("lg1", "1"));

        Assert.NotNull(service.GetPersonalKnowledge("lg1", "1"));
        Assert.Null(service.GetPersonalKnowledge("lg2", "1"));
    }

    [Fact]
    public void Player_Vs_Player_Evidence_Is_Created_For_Available_Alternatives()
    {
        var owner = Owner("owner-a", 1, "Alpha");
        var draft = ImportedDraft("d1");
        var prefs = PersonalDraftLearningPolicy.ExtractPreferences(draft, owner);

        Assert.Contains(prefs, p =>
            p.PreferredPlayerName == "Chosen RB"
            && p.PassedPlayerName == "Passed WR"
            && p.ObservationCount == 1);
        Assert.DoesNotContain(prefs, p => p.PreferredPlayerName == "Passed WR");
        Assert.All(prefs.Where(p => p.PreferredPlayerName == "Chosen RB"), p =>
            Assert.Equal(0, p.Context.RosterBefore.GetValueOrDefault("RB")));
    }

    [Fact]
    public void Repeated_Preference_Strengthens_Evidence()
    {
        var request = Scope("lg1", "1");
        var owner = Owner("owner-a", 1, "Alpha");
        var first = PersonalDraftLearningPolicy.Merge(
            PersonalDraftLearningPolicy.Empty(request, owner),
            ImportedDraft("d1"),
            owner,
            request,
            PersonalDraftLearningPolicy.ExtractPreferences(ImportedDraft("d1"), owner));
        var once = first.Preferences.Single(p => p.PreferredPlayerName == "Chosen RB" && p.PassedPlayerName == "Passed WR");
        Assert.Equal(1, once.ObservationCount);

        var second = PersonalDraftLearningPolicy.Merge(
            first,
            ImportedDraft("d2"),
            owner,
            request,
            PersonalDraftLearningPolicy.ExtractPreferences(ImportedDraft("d2"), owner));
        var twice = second.Preferences.Single(p => p.PreferredPlayerName == "Chosen RB" && p.PassedPlayerName == "Passed WR");
        Assert.Equal(2, twice.ObservationCount);
        Assert.Equal(HistoricalEvidenceStrength.Insufficient, PersonalDraftLearningPolicy.Strength(twice.ObservationCount));
        Assert.True(PersonalDraftLearningPolicy.Strength(6) > PersonalDraftLearningPolicy.Strength(2));
    }

    [Fact]
    public void Contextual_Contradiction_Remains_Separated()
    {
        var lowWr = Context(wr: 0);
        var highWr = Context(wr: 3);
        var preferB = new PersonalPlayerPreference("b", "B", "a", "A", lowWr, 1, ["d1"]);
        var preferA = new PersonalPlayerPreference("a", "A", "b", "B", highWr, 1, ["d2"]);

        Assert.NotEqual(PersonalDraftLearningPolicy.ContextKey(lowWr), PersonalDraftLearningPolicy.ContextKey(highWr));

        var request = Scope("lg1", "1");
        var owner = Owner("owner-a", 1, "Alpha");
        var merged = PersonalDraftLearningPolicy.Merge(
            PersonalDraftLearningPolicy.Empty(request, owner),
            ImportedDraft("d1"),
            owner,
            request,
            [preferB]);
        merged = PersonalDraftLearningPolicy.Merge(merged, ImportedDraft("d2"), owner, request, [preferA]);

        Assert.Equal(2, merged.Preferences.Count);
        Assert.Contains(merged.Preferences, p => p.PreferredPlayerKey == "b" && PersonalDraftLearningPolicy.ContextKey(p.Context) == PersonalDraftLearningPolicy.ContextKey(lowWr));
        Assert.Contains(merged.Preferences, p => p.PreferredPlayerKey == "a" && PersonalDraftLearningPolicy.ContextKey(p.Context) == PersonalDraftLearningPolicy.ContextKey(highWr));
    }

    [Fact]
    public void Weak_Evidence_Cannot_Overwhelm_A_Major_Objective_Value_Gap()
    {
        var knowledge = KnowledgeWithPreference("b", "B", "a", "A", observations: 1);
        var available = new List<(string Key, string Name, decimal Projection)>
        {
            ("a", "A", 20m),
            ("b", "B", 10m)
        };
        var adjustment = PersonalDraftLearningPolicy.Adjust(
            "b", "B", 10m, available, knowledge, Context(wr: 0));

        Assert.Equal(0m, adjustment.ScoreDelta);
    }

    [Fact]
    public void Persistence_Survives_Restart()
    {
        var histFile = Temp("hist");
        var personalFile = Temp("personal");
        var store = new HistoricalLeagueDraftStore(NullLogger<HistoricalLeagueDraftStore>.Instance, Path.GetFileName(histFile));
        var personal = new PersonalDraftKnowledgeStore(NullLogger<PersonalDraftKnowledgeStore>.Instance, Path.GetFileName(personalFile));
        // The stores combine PLAYBOOK_DATA_DIR / base dir with the file name. Use unique names already tracked.
        var service = NewService(store, personal, new FakeSleeper { Draft = CompletedDraft(), Picks = ThreePicks(), LeagueSnapshot = LeagueSnapshot() });

        var learned = service.LearnFromImportedDraft(ImportedDraft("persist-1"), Scope("lg1", "1"));
        Assert.NotNull(learned);

        var restarted = NewService(store, personal, new FakeSleeper());
        var reloaded = restarted.GetPersonalKnowledge("lg1", "1");
        Assert.NotNull(reloaded);
        Assert.Equal(learned!.DraftCount, reloaded!.DraftCount);
        Assert.Equal(learned.DecisionCount, reloaded.DecisionCount);
        Assert.Equal(learned.Preferences.Count, reloaded.Preferences.Count);
    }

    [Fact]
    public void Unknown_Owner_Creates_No_Personal_Learning()
    {
        var (_, personal, service) = NewLiveImport();
        var draft = ImportedDraft("d1");
        var result = service.LearnFromImportedDraft(
            draft,
            new PersonalDraftLearningRequest("lg1", "99", "Boys League", "Ghost", 99, "nobody", "Ghost"));
        Assert.Null(result);
        Assert.Empty(personal.Load());
    }

    [Fact]
    public void Close_Call_Uses_Strong_Contextual_Player_Preference()
    {
        var knowledge = KnowledgeWithPreference("b", "B", "a", "A", observations: 8);
        var available = new List<(string Key, string Name, decimal Projection)>
        {
            ("a", "A", 15.2m),
            ("b", "B", 15.0m)
        };

        var forB = PersonalDraftLearningPolicy.Adjust("b", "B", 15.0m, available, knowledge, Context(wr: 0));
        var forA = PersonalDraftLearningPolicy.Adjust("a", "A", 15.2m, available, knowledge, Context(wr: 0));

        Assert.True(forB.ScoreDelta > 0);
        Assert.True(forA.ScoreDelta < 0);
        Assert.True(15.0m + forB.ScoreDelta > 15.2m + forA.ScoreDelta);
        Assert.Equal(
            PersonalDraftLearningPolicy.HistorySentence("B", "A", 8),
            forB.Factor!.Detail);
    }

    [Fact]
    public void One_Imported_Decision_Moves_A_Close_Call()
    {
        var knowledge = KnowledgeWithPreference("b", "B", "a", "A", observations: 1);
        var available = new List<(string Key, string Name, decimal Projection)>
        {
            ("a", "A", 15.1m),
            ("b", "B", 14.9m)
        };

        var forB = PersonalDraftLearningPolicy.Adjust("b", "B", 14.9m, available, knowledge, Context(wr: 0));
        var forA = PersonalDraftLearningPolicy.Adjust("a", "A", 15.1m, available, knowledge, Context(wr: 0));

        Assert.True(forB.ScoreDelta > 0);
        Assert.True(14.9m + forB.ScoreDelta > 15.1m + forA.ScoreDelta);
        Assert.Contains("You selected B over A in 1 similar decision.", forB.Factor!.Detail);
    }

    [Fact]
    public void Similar_Roster_Context_Still_Applies_The_Preference()
    {
        var knowledge = KnowledgeWithPreference("b", "B", "a", "A", observations: 1);
        var available = new List<(string Key, string Name, decimal Projection)>
        {
            ("a", "A", 15.0m),
            ("b", "B", 15.0m)
        };

        var matching = PersonalDraftLearningPolicy.Adjust("b", "B", 15.0m, available, knowledge, Context(wr: 0));
        var similar = PersonalDraftLearningPolicy.Adjust("b", "B", 15.0m, available, knowledge, Context(wr: 1));

        Assert.True(matching.ScoreDelta > 0);
        Assert.True(similar.ScoreDelta > 0);
        Assert.True(similar.ScoreDelta <= matching.ScoreDelta);
        Assert.NotNull(similar.Factor);
    }

    [Fact]
    public void Unknown_Scoring_From_A_Stale_Import_Still_Matches_Live_Ppr()
    {
        var unknown = Context(wr: 0) with { ScoringFormat = "Unknown" };
        var knowledge = new PersonalDraftKnowledge
        {
            LeagueId = "lg1",
            TeamId = "1",
            LeagueName = "Boys League",
            TeamName = "Alpha",
            DraftCount = 1,
            DecisionCount = 1,
            Preferences = [new PersonalPlayerPreference("b", "B", "a", "A", unknown, 1, ["d1"])]
        };
        var available = new List<(string Key, string Name, decimal Projection)>
        {
            ("a", "A", 15.0m),
            ("b", "B", 15.0m)
        };

        var adjustment = PersonalDraftLearningPolicy.Adjust("b", "B", 15.0m, available, knowledge, Context(wr: 0));

        Assert.True(adjustment.ScoreDelta > 0);
        Assert.NotNull(adjustment.Factor);
    }

    [Fact]
    public void Different_League_Type_Does_Not_Apply_The_Preference()
    {
        var knowledge = KnowledgeWithPreference("b", "B", "a", "A", observations: 8);
        var available = new List<(string Key, string Name, decimal Projection)>
        {
            ("a", "A", 15.0m),
            ("b", "B", 15.0m)
        };
        var dynasty = Context(wr: 0) with { LeagueType = LeagueType.Dynasty };

        var adjustment = PersonalDraftLearningPolicy.Adjust(
            "b", "B", 15.0m, available, knowledge, dynasty);

        Assert.Equal(0m, adjustment.ScoreDelta);
        Assert.Null(adjustment.Factor);
    }

    [Fact]
    public void Contextual_Contradiction_Applies_Only_The_Matching_Roster_Preference()
    {
        var knowledge = new PersonalDraftKnowledge
        {
            LeagueId = "lg1",
            TeamId = "1",
            LeagueName = "Boys League",
            TeamName = "Alpha",
            DraftCount = 2,
            DecisionCount = 8,
            Preferences =
            [
                new PersonalPlayerPreference("a", "A", "b", "B", Context(wr: 0), 4, ["d-low"]),
                new PersonalPlayerPreference("b", "B", "a", "A", Context(wr: 3), 4, ["d-high"])
            ]
        };
        var available = new List<(string Key, string Name, decimal Projection)>
        {
            ("a", "A", 15.0m),
            ("b", "B", 15.0m)
        };

        var atLowWr = PersonalDraftLearningPolicy.Adjust("a", "A", 15.0m, available, knowledge, Context(wr: 0));
        var atHighWr = PersonalDraftLearningPolicy.Adjust("a", "A", 15.0m, available, knowledge, Context(wr: 3));

        Assert.True(atLowWr.ScoreDelta > 0);
        Assert.Contains("You selected A over B", atLowWr.Factor!.Detail);
        Assert.True(atHighWr.ScoreDelta < 0);
        Assert.Contains("You selected B over A", atHighWr.Factor!.Detail);
    }

    public void Dispose()
    {
        foreach (var file in _files)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* test cleanup */ }
        }
    }

    private (HistoricalLeagueDraftStore store, PersonalDraftKnowledgeStore personal, HistoricalLeagueIntelligenceService service) NewLiveImport()
    {
        var store = NewHistStore();
        var personal = NewPersonalStore();
        var sleeper = new FakeSleeper { Draft = CompletedDraft(), Picks = ThreePicks(), LeagueSnapshot = LeagueSnapshot() };
        return (store, personal, NewService(store, personal, sleeper));
    }

    private (HistoricalLeagueDraftStore store, PersonalDraftKnowledgeStore personal, HistoricalLeagueIntelligenceService service) NewServices()
    {
        var store = NewHistStore();
        var personal = NewPersonalStore();
        return (store, personal, NewService(store, personal, new FakeSleeper()));
    }

    private HistoricalLeagueDraftStore NewHistStore() =>
        new(NullLogger<HistoricalLeagueDraftStore>.Instance, Temp("hist"));

    private PersonalDraftKnowledgeStore NewPersonalStore() =>
        new(NullLogger<PersonalDraftKnowledgeStore>.Instance, Temp("personal"));

    private static HistoricalLeagueIntelligenceService NewService(
        HistoricalLeagueDraftStore store, PersonalDraftKnowledgeStore personal, ISleeperLeagueClient sleeper) =>
        new(store, sleeper, new PlayerIdentityDirectory(), personal);

    private string Temp(string prefix)
    {
        var name = $"{prefix}-{Guid.NewGuid():N}.json";
        _files.Add(Path.Combine(AppContext.BaseDirectory, "data", name));
        return name;
    }

    private static PersonalDraftLearningRequest Scope(string leagueId, string teamId, int rosterId = 1, string ownerUserId = "user-a") =>
        new(leagueId, teamId, "Boys League", "Alpha", rosterId, ownerUserId, "Alpha");

    private static HistoricalOwner Owner(string userId, int rosterId, string name) =>
        new() { SleeperUserId = userId, RosterId = rosterId, DisplayName = name };

    private static HistoricalLeagueDraft ImportedDraft(string id) => new()
    {
        HistoricalDraftId = id,
        LeagueId = "source-league",
        Season = "2024",
        LeagueName = "Source",
        LeagueType = LeagueType.Redraft,
        DraftType = "snake",
        TeamCount = 2,
        RoundCount = 1,
        ScoringSettings = new Dictionary<string, double> { ["rec"] = 1 },
        RosterSettings = ["RB", "WR"],
        Owners =
        [
            Owner("user-a", 1, "Alpha"),
            Owner("user-b", 2, "Beta")
        ],
        Picks =
        [
            new HistoricalDraftPick
            {
                PickNumber = 1, Round = 1, DraftSlot = 1, OwnerKey = "user-a", OwnerName = "Alpha",
                SleeperUserId = "user-a", RosterId = 1, SleeperPlayerId = "p-chosen",
                PlaybookPlayerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                PlayerName = "Chosen RB", Position = "RB"
            },
            new HistoricalDraftPick
            {
                PickNumber = 2, Round = 1, DraftSlot = 2, OwnerKey = "user-b", OwnerName = "Beta",
                SleeperUserId = "user-b", RosterId = 2, SleeperPlayerId = "p-passed",
                PlaybookPlayerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                PlayerName = "Passed WR", Position = "WR"
            }
        ],
        IsComplete = true
    };

    private static PersonalPreferenceContext Context(int wr) => new(
        LeagueType.Redraft, "PPR", 12, 1, 1,
        new Dictionary<string, int> { ["WR"] = wr, ["RB"] = 0, ["TE"] = 0, ["QB"] = 0 },
        ["a", "b"]);

    private static PersonalDraftKnowledge KnowledgeWithPreference(
        string preferredKey, string preferredName, string passedKey, string passedName, int observations) => new()
    {
        LeagueId = "lg1",
        TeamId = "1",
        LeagueName = "Boys League",
        TeamName = "Alpha",
        DraftCount = 1,
        DecisionCount = observations,
        Preferences =
        [
            new PersonalPlayerPreference(preferredKey, preferredName, passedKey, passedName, Context(wr: 0), observations, ["d1"])
        ]
    };

    private static SleeperDraftSnapshot CompletedDraft(string draftId = "123456789012") => new()
    {
        DraftId = draftId,
        LeagueId = "source-league",
        Season = "2024",
        Status = "complete",
        Type = "snake",
        Rounds = 1,
        Teams = 2,
        DraftOrderByUserId = new Dictionary<string, int> { ["user-a"] = 1, ["user-b"] = 2 },
        SlotToRosterId = new Dictionary<int, int> { [1] = 1, [2] = 2 },
        RosterPositions = ["RB", "WR"],
        ScoringType = "ppr",
        LeagueTypeRaw = "0",
        Name = "Completed"
    };

    private static List<SleeperDraftPickSnapshot> ThreePicks() =>
    [
        new() { PickNumber = 1, Round = 1, DraftSlot = 1, RosterId = 1, PickedByUserId = "user-a", SleeperPlayerId = "p-chosen", PlayerName = "Chosen RB", Position = "RB" },
        new() { PickNumber = 2, Round = 1, DraftSlot = 2, RosterId = 2, PickedByUserId = "user-b", SleeperPlayerId = "p-passed", PlayerName = "Passed WR", Position = "WR" }
    ];

    private static SleeperLeagueSnapshot LeagueSnapshot() => new()
    {
        ExternalLeagueId = "source-league",
        Name = "Source",
        Season = "2024",
        Status = "complete",
        TeamCount = 2,
        CurrentWeek = 1,
        SleeperLeagueType = 0,
        ScoringSettings = new Dictionary<string, double> { ["rec"] = 1 },
        RosterPositions = ["RB", "WR"],
        Rosters =
        [
            new SleeperRosterSnapshot { RosterId = 1, OwnerId = "user-a", TeamName = "Alpha", OwnerName = "Alpha", SleeperPlayerIds = [], StarterSleeperPlayerIds = [], ReserveSleeperPlayerIds = [], TaxiSleeperPlayerIds = [] },
            new SleeperRosterSnapshot { RosterId = 2, OwnerId = "user-b", TeamName = "Beta", OwnerName = "Beta", SleeperPlayerIds = [], StarterSleeperPlayerIds = [], ReserveSleeperPlayerIds = [], TaxiSleeperPlayerIds = [] }
        ]
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
}
