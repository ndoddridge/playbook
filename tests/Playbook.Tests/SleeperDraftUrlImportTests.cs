using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Core.Leagues;
using Playbook.Infrastructure.Historical;
using Playbook.Infrastructure.Players;

namespace Playbook.Tests;

/// <summary>
/// Importing a single completed Sleeper draft by pasted URL/id, without a connected league —
/// the "past draft" half of draft ingestion. Reuses the same BuildSleeperDraft mapper and
/// ImportAsync/ValidateAndReconstruct pipeline the league-history import already uses.
/// </summary>
public class SleeperDraftUrlImportTests
{
    [Fact]
    public async Task Rejects_Input_That_Is_Not_A_Usable_Sleeper_Draft_Link()
    {
        var service = NewService(NewStore(), new FakeSleeper());

        var result = await service.ImportSleeperDraftByIdAsync("not a draft link");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, x => x.Contains("doesn't look like", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Rejects_A_Draft_That_Sleeper_Cannot_Find()
    {
        var service = NewService(NewStore(), new FakeSleeper { Draft = null });

        var result = await service.ImportSleeperDraftByIdAsync("123456789012");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, x => x.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Rejects_A_Draft_That_Is_Still_In_Progress_And_Points_To_Follow_Instead()
    {
        var sleeper = new FakeSleeper { Draft = Draft(status: "drafting"), Picks = [] };
        var service = NewService(NewStore(), sleeper);

        var result = await service.ImportSleeperDraftByIdAsync("123456789012");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, x => x.Contains("Follow", StringComparison.Ordinal));
        Assert.Empty(service.GetDrafts("league-x"));
    }

    [Fact]
    public async Task Imports_A_Completed_Draft_With_No_Connected_League_And_Persists_It()
    {
        var store = NewStore();
        var sleeper = new FakeSleeper { Draft = Draft(status: "complete"), Picks = TwoPicks(), LeagueSnapshot = null };
        var service = NewService(store, sleeper);

        var result = await service.ImportSleeperDraftByIdAsync("https://sleeper.com/draft/nfl/123456789012");

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(2, result.Draft!.Picks.Count);
        Assert.Equal("url-import", result.Draft.Source);

        // Auto-persisted with no second step: a fresh service instance over the same file sees it.
        var reloaded = NewService(store, sleeper).GetDrafts("league-x");
        Assert.Single(reloaded);
        Assert.Equal(2, reloaded[0].Picks.Count);

        File.Delete(store.StorePath);
    }

    [Fact]
    public async Task Imported_Draft_Feeds_The_Same_Knowledge_Queries_As_A_Regular_Import()
    {
        var store = NewStore();
        var sleeper = new FakeSleeper { Draft = Draft(status: "complete"), Picks = TwoPicks(), LeagueSnapshot = null };
        var service = NewService(store, sleeper);

        await service.ImportSleeperDraftByIdAsync("123456789012");

        var tendencies = service.GetOwnerTendencies("league-x");
        Assert.NotEmpty(tendencies);
        var ranges = service.GetLeaguePositionTendencies("league-x");
        Assert.Contains(ranges, r => r.Position == "RB");

        File.Delete(store.StorePath);
    }

    /// <summary>
    /// Regression for a real crash: Sleeper omits the top-level league_id for a mock draft's
    /// auto-created league (SleeperLeagueClient maps that to ""), and the real
    /// SleeperLeagueClient.GetLeagueSnapshotAsync throws ArgumentException on a blank id. Verified
    /// against a real completed mock draft (sleeper.app/draft/nfl/1395528272707612672) that
    /// reproduced "An unhandled error has occurred" in the UI before this fix.
    /// </summary>
    [Fact]
    public async Task Imports_A_Mock_Draft_Whose_League_Id_Is_Blank_Without_Throwing()
    {
        var store = NewStore();
        var draft = Draft(status: "complete", leagueId: "");
        var sleeper = new FakeSleeper { Draft = draft, Picks = TwoPicks() };
        var service = NewService(store, sleeper);

        var result = await service.ImportSleeperDraftByIdAsync("123456789012");

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(2, result.Draft!.Picks.Count);
        // Never fabricated: with no roster/owner data published by Sleeper for this pick shape,
        // ownership is reported as unresolved rather than guessed.
        Assert.All(result.Draft.Picks, p => Assert.StartsWith("unresolved:", p.OwnerKey, StringComparison.Ordinal));

        File.Delete(store.StorePath);
    }

    private static SleeperDraftSnapshot Draft(string status, string leagueId = "league-x") => new()
    {
        DraftId = "123456789012",
        LeagueId = leagueId,
        Season = "2024",
        Status = status,
        Type = "snake",
        Rounds = 1,
        Teams = 2,
        DraftOrderByUserId = new Dictionary<string, int>(),
        SlotToRosterId = new Dictionary<int, int>(),
        RosterPositions = ["QB", "RB", "WR"],
        ScoringType = "ppr",
        LeagueTypeRaw = "0",
        Name = "Test Draft"
    };

    private static List<SleeperDraftPickSnapshot> TwoPicks() =>
    [
        new() { PickNumber = 1, Round = 1, DraftSlot = 1, RosterId = 1, SleeperPlayerId = "p1", PlayerName = "Player One", Position = "RB" },
        new() { PickNumber = 2, Round = 1, DraftSlot = 2, RosterId = 2, SleeperPlayerId = "p2", PlayerName = "Player Two", Position = "WR" }
    ];

    private static HistoricalLeagueDraftStore NewStore() =>
        new(NullLogger<HistoricalLeagueDraftStore>.Instance, $"historical-url-import-tests-{Guid.NewGuid():N}.json");

    private static HistoricalLeagueIntelligenceService NewService(HistoricalLeagueDraftStore store, ISleeperLeagueClient sleeper) =>
        new(store, sleeper, new PlayerIdentityDirectory());

    private sealed class FakeSleeper : ISleeperLeagueClient
    {
        public SleeperDraftSnapshot? Draft { get; set; }
        public IReadOnlyList<SleeperDraftPickSnapshot> Picks { get; set; } = [];
        public SleeperLeagueSnapshot? LeagueSnapshot { get; set; }

        public Task<SleeperLeagueSnapshot?> GetLeagueSnapshotAsync(string leagueId, CancellationToken cancellationToken = default)
        {
            // Mirrors the real SleeperLeagueClient: ArgumentException.ThrowIfNullOrWhiteSpace(leagueId).
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
