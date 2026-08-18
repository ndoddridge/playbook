using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Historical;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Core.Historical;
using Playbook.Core.Leagues;
using Playbook.Infrastructure.Historical;
using Playbook.Infrastructure.Players;

namespace Playbook.Tests;

public class HistoricalLeagueIntelligenceTests
{
    [Fact]
    public async Task Import_Reconstructs_Roster_Before_Each_Pick_And_Persists()
    {
        var store = NewStore(); var service = NewService(store);
        var result = await service.ImportAsync(Draft());
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var picks = result.Draft!.Picks;
        Assert.Empty(picks[0].RosterBefore);
        Assert.Equal(1, picks[2].RosterBefore["RB"]);
        var reloaded = NewService(store).GetDrafts("league-a");
        Assert.Single(reloaded);
        Assert.Equal(3, reloaded[0].Picks.Count);
        File.Delete(store.StorePath);
    }

    [Fact]
    public async Task Import_Rejects_Duplicates_Impossible_Picks_And_Missing_Player_Identity()
    {
        var baseDraft = Draft();
        var draft = new HistoricalLeagueDraft { HistoricalDraftId = baseDraft.HistoricalDraftId, LeagueId = baseDraft.LeagueId, Season = baseDraft.Season, LeagueName = baseDraft.LeagueName, LeagueType = baseDraft.LeagueType, DraftType = baseDraft.DraftType, TeamCount = baseDraft.TeamCount, RoundCount = baseDraft.RoundCount, ScoringSettings = baseDraft.ScoringSettings, RosterSettings = baseDraft.RosterSettings, Owners = baseDraft.Owners, IsComplete = baseDraft.IsComplete, Picks = [Pick(1, 1, "owner-a", "RB"), Pick(1, 1, "owner-b", "WR", sleeperPlayerId: null)] };
        var result = await NewService(NewStore()).ImportAsync(draft);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, x => x.Contains("Duplicate", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Contains("identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Incomplete_Draft_Is_Retained_With_An_Explicit_Warning()
    {
        var result = await NewService(NewStore()).ImportAsync(Draft());
        Assert.True(result.Succeeded);
        Assert.False(result.Draft!.IsComplete);
        Assert.Contains(result.Warnings, x => x.Contains("Incomplete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Owner_Identity_Uses_Stable_Id_And_Does_Not_Merge_Display_Names()
    {
        var service = NewService(NewStore());
        await service.ImportAsync(Draft("2024", "owner-a", "Same Name"));
        await service.ImportAsync(Draft("2025", "owner-b", "Same Name"));
        var tendencies = service.GetOwnerTendencies("league-a");
        Assert.Contains(tendencies, x => x.OwnerKey == "owner-a");
        Assert.Contains(tendencies, x => x.OwnerKey == "owner-b");
    }

    [Fact]
    public async Task Redraft_Analytics_Exclude_Dynasty_And_One_Selection_Is_Insufficient()
    {
        var service = NewService(NewStore());
        await service.ImportAsync(Draft("2024", "owner-a", "A", LeagueType.Redraft));
        await service.ImportAsync(Draft("2025", "owner-a", "A", LeagueType.Dynasty));
        var tendencies = service.GetOwnerTendencies("league-a", LeagueType.Redraft);
        Assert.All(tendencies, x => Assert.Equal(LeagueType.Redraft, x.LeagueType));
        Assert.All(tendencies, x => Assert.Equal(HistoricalEvidenceStrength.Insufficient, x.EvidenceStrength));
        var history = service.GetPlayerHistory("league-a", "player-1");
        Assert.Equal(1, history.DraftCount);
        Assert.Equal(HistoricalEvidenceStrength.Insufficient, history.EvidenceStrength);
    }

    [Fact]
    public async Task Time_Correct_Snapshot_Joins_Picks_And_Newer_Adp_Cannot_Leak_Backward()
    {
        var service = NewService(NewStore());
        await service.ImportAsync(Draft("2024", draftedAt: new DateTimeOffset(2024, 8, 20, 0, 0, 0, TimeSpan.Zero)));
        await service.ImportAdpSnapshotAsync(Snapshot("2024", new DateTimeOffset(2024, 8, 10, 0, 0, 0, TimeSpan.Zero), 12));
        var comparison = service.GetPickMarketComparisons("league-a").First(x => x.PlayerKey == "player-1");
        Assert.Equal(12, comparison.Adp);
        Assert.Equal(HistoricalMarketValueClassification.Reach, comparison.Classification);
        await service.ImportAdpSnapshotAsync(Snapshot("2024", new DateTimeOffset(2024, 9, 10, 0, 0, 0, TimeSpan.Zero), 99));
        comparison = service.GetPickMarketComparisons("league-a").First(x => x.PlayerKey == "player-1");
        Assert.Equal(12, comparison.Adp); // Current/later data is never substituted into a past draft.
    }

    [Fact]
    public async Task Range_Owner_Signal_And_Availability_Remain_Descriptive_And_Require_Three_Drafts()
    {
        var service = NewService(NewStore());
        foreach (var season in new[] { "2022", "2023", "2024" }) await service.ImportAsync(Draft(season, draftedAt: new DateTimeOffset(int.Parse(season), 8, 20, 0, 0, 0, TimeSpan.Zero)));
        var range = Assert.Single(service.GetLeaguePlayerRanges("league-a"), x => x.PlayerKey == "player-1");
        Assert.Equal(3, range.DraftCount); Assert.Equal(1, range.MinimumPick); Assert.Equal(1, range.MaximumPick);
        var target = service.QueryTargetPlayer("league-a", "player-1", 1, 12);
        Assert.Equal(HistoricalAvailabilityAssessment.Low, target.Availability.Assessment);
        Assert.Contains(service.GetOwnerMarketSignals("league-a"), x => x.OwnerKey == "owner-a" && x.SelectionCount >= 3);
    }

    [Fact]
    public async Task Snapshot_Rejects_Unidentified_Players_And_Does_Not_Mix_Dynasty()
    {
        var service = NewService(NewStore()); await service.ImportAsync(Draft("2024", type: LeagueType.Dynasty, draftedAt: new DateTimeOffset(2024, 8, 20, 0, 0, 0, TimeSpan.Zero)));
        var result = await service.ImportAdpSnapshotAsync(Snapshot("2024", new DateTimeOffset(2024, 8, 10, 0, 0, 0, TimeSpan.Zero), 12, LeagueType.Redraft));
        Assert.False(result.Succeeded);
        var invalid = new HistoricalAdpSnapshot { LeagueId = "league-a", Season = "2024", Source = "test", SnapshotDateUtc = DateTimeOffset.UtcNow, LeagueType = LeagueType.Dynasty, ScoringFormat = "HalfPPR", Players = [new HistoricalAdpRecord { PlayerName = "X", Position = "RB", Adp = 1 }] };
        Assert.False((await service.ImportAdpSnapshotAsync(invalid)).Succeeded);
    }

    private static HistoricalLeagueDraft Draft(string season = "2024", string ownerId = "owner-a", string ownerName = "A", LeagueType type = LeagueType.Redraft, DateTimeOffset? draftedAt = null) => new()
    {
        HistoricalDraftId = $"draft-{season}-{ownerId}", LeagueId = "league-a", Season = season, LeagueName = "Boys League", LeagueType = type, DraftType = "snake",
        TeamCount = 2, RoundCount = 2, ScoringSettings = new Dictionary<string, double> { ["rec"] = .5 }, RosterSettings = ["QB", "RB", "WR", "FLEX"],
        Owners = [new HistoricalOwner { SleeperUserId = ownerId, DisplayName = ownerName, RosterId = 1 }, new HistoricalOwner { SleeperUserId = "owner-c", DisplayName = "C", RosterId = 2 }],
        Picks = [Pick(1, 1, ownerId, "RB", ownerName), Pick(2, 1, "owner-c", "WR", "C", "player-2"), Pick(3, 2, ownerId, "WR", ownerName, "player-3")], IsComplete = true, DraftedAtUtc = draftedAt
    };
    private static HistoricalAdpSnapshot Snapshot(string season, DateTimeOffset at, double adp, LeagueType type = LeagueType.Redraft) => new() { LeagueId = "league-a", Season = season, Source = "test", SnapshotDateUtc = at, LeagueType = type, ScoringFormat = "HalfPPR", SourceQuality = HistoricalSourceQuality.Established, Players = [new HistoricalAdpRecord { SleeperPlayerId = "player-1", PlayerName = "Player 1", Position = "RB", OverallRank = 12, PositionRank = 4, Adp = adp }] };
    private static HistoricalDraftPick Pick(int number, int round, string owner, string position, string name = "A", string? sleeperPlayerId = "player-1") => new()
    { PickNumber = number, Round = round, DraftSlot = number % 2 == 0 ? 2 : 1, OwnerKey = owner, OwnerName = name, SleeperUserId = owner, SleeperPlayerId = sleeperPlayerId, PlayerName = $"Player {number}", Position = position };
    private static HistoricalLeagueDraftStore NewStore() => new(NullLogger<HistoricalLeagueDraftStore>.Instance, $"historical-tests-{Guid.NewGuid():N}.json");
    private static HistoricalLeagueIntelligenceService NewService(HistoricalLeagueDraftStore store) => new(store, new NullSleeper(), new PlayerIdentityDirectory());
    private sealed class NullSleeper : ISleeperLeagueClient
    { public Task<SleeperLeagueSnapshot?> GetLeagueSnapshotAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<SleeperLeagueSnapshot?>(null); public Task<IReadOnlyList<SleeperDraftSummary>> GetDraftsForLeagueAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SleeperDraftSummary>>([]); public Task<SleeperDraftSnapshot?> GetDraftAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<SleeperDraftSnapshot?>(null); public Task<IReadOnlyList<SleeperDraftPickSnapshot>> GetDraftPicksAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SleeperDraftPickSnapshot>>([]); }
}
