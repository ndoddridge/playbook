using Playbook.Application.Leagues.Sleeper;
using Playbook.Core.Draft;
using Playbook.Core.Leagues;
using Playbook.Infrastructure.Draft;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// Following a Sleeper draft directly by link — the capability that makes mock drafts work.
///
/// Context for why this exists: a Sleeper mock draft lives in its own auto-created league, which
/// the user has not connected. Resolving a draft from the connected league therefore can never
/// find it, no matter how long Playbook polls. Verified against the user's real mock draft.
/// </summary>
public class SleeperDraftAttachTests
{
    // ---------------------------------------------------------------- link parsing

    [Theory]
    [InlineData("https://sleeper.com/draft/nfl/1394906274017079296", "1394906274017079296")]
    [InlineData("https://sleeper.app/draft/nfl/1394906274017079296", "1394906274017079296")]
    [InlineData("sleeper.com/draft/nfl/1394906274017079296", "1394906274017079296")]
    [InlineData("1394906274017079296", "1394906274017079296")]
    [InlineData("  https://sleeper.com/draft/nfl/1394906274017079296  ", "1394906274017079296")]
    [InlineData("https://sleeper.com/draft/nfl/1394906274017079296?foo=bar", "1394906274017079296")]
    public void TryParse_ExtractsDraftId_FromRealSleeperLinkShapes(string input, string expected)
    {
        Assert.True(SleeperDraftLink.TryParse(input, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://sleeper.com/leagues")]
    [InlineData("12345")] // too short to be a Sleeper snowflake
    public void TryParse_RejectsInputWithNoUsableDraftId(string? input)
    {
        Assert.False(SleeperDraftLink.TryParse(input, out var id));
        Assert.Equal("", id);
    }

    // ---------------------------------------------------------------- self-describing draft

    [Fact]
    public void BuildLeagueFromDraft_DerivesEverythingFromTheDraftItself()
    {
        // Shape taken from the user's real mock draft: 10 teams, 16 rounds, ppr, league_type 0.
        var draft = Draft(
            teams: 10,
            rounds: 16,
            scoringType: "ppr",
            leagueTypeRaw: "0",
            rosterPositions: ["QB", "RB", "RB", "WR", "WR", "TE", "FLEX", "FLEX", "K", "DEF", "BN"]);

        var league = DraftAssistantService.BuildLeagueFromDraft(draft);

        Assert.Equal(10, league.NumberOfTeams);
        Assert.Equal(LeagueType.Redraft, league.LeagueType);
        Assert.Equal(ScoringType.Ppr, league.ScoringType);
        Assert.Equal(2026, league.Season);
        Assert.Equal(LeagueDataSource.Sleeper, league.DataSource);
        Assert.Contains("QB", league.RosterPositions);
        Assert.Equal(2, league.RosterPositions.Count(p => p == "RB"));
    }

    [Fact]
    public void BuildLeagueFromDraft_IsStableAcrossPolls_SoStrategySelectionSurvives()
    {
        var draft = Draft();

        var first = DraftAssistantService.BuildLeagueFromDraft(draft);
        var second = DraftAssistantService.BuildLeagueFromDraft(draft);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void BuildLeagueFromDraft_MapsDynastyDraftMetadata()
    {
        var league = DraftAssistantService.BuildLeagueFromDraft(Draft(leagueTypeRaw: "2"));

        Assert.Equal(LeagueType.Dynasty, league.LeagueType);
    }

    // ---------------------------------------------------------------- slot resolution

    [Fact]
    public void SlotToRosterId_PrefersTheDraftsOwnMapping_SoItWorksWithNoLeague()
    {
        // The real mock draft publishes slot_to_roster_id directly; there is no connected league
        // to cross-reference, so this mapping is the only way to identify whose pick is whose.
        var draft = Draft(slotToRoster: new Dictionary<int, int> { [1] = 2, [2] = 3, [8] = 1 });

        var map = DraftAssistantService.BuildSlotToRosterId(draft, leagueSnapshot: null);

        Assert.Equal(2, map[1]);
        Assert.Equal(1, map[8]);
    }

    [Fact]
    public void SlotToRosterId_EmptyWhenNeitherSourceIsAvailable()
    {
        var map = DraftAssistantService.BuildSlotToRosterId(Draft(), leagueSnapshot: null);

        Assert.Empty(map);
    }

    // ---------------------------------------------------------------- user identification

    [Fact]
    public void ResolveAttachedUserRosterId_UsesRealDraftOrder_NeverGuesses()
    {
        // The user's Sleeper id comes from the roster they own in a connected league; their slot
        // in the attached draft comes from that draft's own draft_order.
        var draft = Draft(
            draftOrder: new Dictionary<string, int> { ["user-abc"] = 8 },
            slotToRoster: new Dictionary<int, int> { [8] = 1 });

        var snapshot = LeagueSnapshotWithRoster(rosterId: 3, ownerId: "user-abc");
        var connected = ConnectedLeague(selectedRosterId: 3);

        var resolved = DraftAssistantService.ResolveAttachedUserRosterId(draft, snapshot, connected);

        Assert.Equal(1, resolved);
    }

    [Fact]
    public void ResolveAttachedUserRosterId_ReturnsNull_WhenTheUserIsNotInThatDraft()
    {
        // Following someone else's draft must not silently claim a team.
        var draft = Draft(
            draftOrder: new Dictionary<string, int> { ["someone-else"] = 4 },
            slotToRoster: new Dictionary<int, int> { [4] = 9 });

        var snapshot = LeagueSnapshotWithRoster(rosterId: 3, ownerId: "user-abc");
        var connected = ConnectedLeague(selectedRosterId: 3);

        Assert.Null(DraftAssistantService.ResolveAttachedUserRosterId(draft, snapshot, connected));
    }

    [Fact]
    public void ResolveAttachedUserRosterId_ReturnsNull_WithNoConnectedLeague()
    {
        var draft = Draft(draftOrder: new Dictionary<string, int> { ["user-abc"] = 8 });

        Assert.Null(DraftAssistantService.ResolveAttachedUserRosterId(draft, null, null));
    }

    // ---------------------------------------------------------------- helpers

    private static SleeperDraftSnapshot Draft(
        int teams = 10,
        int rounds = 16,
        string? scoringType = "ppr",
        string? leagueTypeRaw = "0",
        IReadOnlyList<string>? rosterPositions = null,
        IReadOnlyDictionary<string, int>? draftOrder = null,
        IReadOnlyDictionary<int, int>? slotToRoster = null) => new()
        {
            DraftId = "1394906274017079296",
            LeagueId = "1394874757479952384",
            Season = "2026",
            Status = "paused",
            Type = "snake",
            Rounds = rounds,
            Teams = teams,
            DraftOrderByUserId = draftOrder ?? new Dictionary<string, int>(),
            SlotToRosterId = slotToRoster ?? new Dictionary<int, int>(),
            RosterPositions = rosterPositions ?? [],
            ScoringType = scoringType,
            LeagueTypeRaw = leagueTypeRaw,
            Name = "Boys test league"
        };

    private static SleeperLeagueSnapshot LeagueSnapshotWithRoster(int rosterId, string ownerId) => new()
    {
        ExternalLeagueId = "lg1",
        Name = "Connected League",
        Season = "2026",
        Status = "pre_draft",
        TeamCount = 10,
        CurrentWeek = 1,
        SleeperLeagueType = 0,
        ScoringSettings = new Dictionary<string, double>(),
        RosterPositions = ["QB", "RB"],
        Rosters =
        [
            new SleeperRosterSnapshot
            {
                RosterId = rosterId,
                OwnerId = ownerId,
                TeamName = "My Team",
                OwnerName = ownerId,
                SleeperPlayerIds = [],
                StarterSleeperPlayerIds = [],
                ReserveSleeperPlayerIds = [],
                TaxiSleeperPlayerIds = []
            }
        ]
    };

    private static League ConnectedLeague(int selectedRosterId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Connected League",
        Platform = LeaguePlatform.Sleeper,
        LeagueType = LeagueType.Redraft,
        ScoringType = ScoringType.Ppr,
        NumberOfTeams = 10,
        CurrentWeek = 1,
        Season = 2026,
        IsActive = true,
        DataSource = LeagueDataSource.Sleeper,
        ExternalId = "lg1",
        SelectedRosterId = selectedRosterId
    };
}
