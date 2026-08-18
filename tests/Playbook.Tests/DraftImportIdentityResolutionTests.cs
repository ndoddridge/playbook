using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Core.Draft;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Infrastructure.Historical;
using Playbook.Infrastructure.Players;

namespace Playbook.Tests;

/// <summary>
/// Resolving screenshot-parsed picks to player/owner identities and routing them to persistent
/// storage (completed draft) or an in-memory-only snapshot (in-progress mock). Never guesses an
/// identity and never persists anything for a mock.
/// </summary>
public class DraftImportIdentityResolutionTests
{
    [Fact]
    public async Task Completed_Draft_Saves_Resolvable_Picks_And_Flags_The_Rest_Without_Fabricating()
    {
        var identities = new PlayerIdentityDirectory();
        identities.Upsert(new PlaybookPlayerIdentity { PlaybookId = Guid.NewGuid(), FullName = "Justin Jefferson", Position = "WR", Team = "MIN" });
        var store = NewStore();
        var service = new HistoricalLeagueIntelligenceService(store, new NullSleeper(), identities);

        var parsed = new DraftImageParseResult(
            [
                new DraftImageParsedPick(1, 1, "Team A", "Justin Jefferson", "WR", false, null),
                new DraftImageParsedPick(2, 1, "Team B", "Some Totally Unknown Player", "RB", false, null),
                new DraftImageParsedPick(3, 1, "Unlisted Team", "Justin Jefferson", "WR", false, null),
                new DraftImageParsedPick(4, 1, "Team A", null, null, true, "Text is too blurry to read.")
            ],
            [],
            false);

        var context = new DraftImportContext(
            "league-img", "2024", "Screenshot League", LeagueType.Redraft, "snake",
            TeamCount: 2, RoundCount: 2,
            ScoringSettings: new Dictionary<string, double>(),
            RosterSettings: [],
            OwnerNames: ["Team A", "Team B"],
            IsCompleteDraft: true);

        var summary = await service.ImportFromImageAsync(parsed, context);

        Assert.Equal(1, summary.SavedCount);
        Assert.Equal(3, summary.FlaggedCount);
        Assert.NotNull(summary.SavedDraft);
        Assert.Null(summary.UnsavedMockDraft);
        Assert.Equal("screenshot", summary.SavedDraft!.Source);
        Assert.Contains(summary.FlaggedDetails, d => d.Contains("Some Totally Unknown Player", StringComparison.Ordinal));
        Assert.Contains(summary.FlaggedDetails, d => d.Contains("Unlisted Team", StringComparison.Ordinal));
        Assert.Contains(summary.FlaggedDetails, d => d.Contains("blurry", StringComparison.Ordinal));

        // Persisted with no second step.
        var reloaded = store.Load();
        Assert.Single(reloaded);

        File.Delete(store.StorePath);
    }

    [Fact]
    public async Task InProgress_Mock_Never_Persists_Anything()
    {
        var identities = new PlayerIdentityDirectory();
        identities.Upsert(new PlaybookPlayerIdentity { PlaybookId = Guid.NewGuid(), FullName = "Justin Jefferson", Position = "WR", Team = "MIN" });
        var store = NewStore();
        var service = new HistoricalLeagueIntelligenceService(store, new NullSleeper(), identities);

        var parsed = new DraftImageParseResult(
            [new DraftImageParsedPick(1, 1, "Team A", "Justin Jefferson", "WR", false, null)], [], false);
        var context = new DraftImportContext(
            "league-img", "2024", "Mock League", LeagueType.Redraft, "snake",
            TeamCount: 2, RoundCount: 2,
            ScoringSettings: new Dictionary<string, double>(), RosterSettings: [],
            OwnerNames: ["Team A", "Team B"], IsCompleteDraft: false);

        var summary = await service.ImportFromImageAsync(parsed, context);

        Assert.Equal(0, summary.SavedCount);
        Assert.Null(summary.SavedDraft);
        Assert.NotNull(summary.UnsavedMockDraft);
        Assert.Single(summary.UnsavedMockDraft!.Picks);
        Assert.Empty(store.Load());

        File.Delete(store.StorePath);
    }

    [Fact]
    public async Task Missing_Pick_Fields_Are_Flagged_Rather_Than_Guessed()
    {
        var service = new HistoricalLeagueIntelligenceService(NewStore(), new NullSleeper(), new PlayerIdentityDirectory());
        var parsed = new DraftImageParseResult(
            [new DraftImageParsedPick(null, null, "Team A", "Someone", "WR", false, null)], [], false);
        var context = new DraftImportContext(
            "league-img", "2024", "League", LeagueType.Redraft, "snake",
            TeamCount: 2, RoundCount: 2, ScoringSettings: new Dictionary<string, double>(), RosterSettings: [],
            OwnerNames: ["Team A", "Team B"], IsCompleteDraft: true);

        var summary = await service.ImportFromImageAsync(parsed, context);

        Assert.Equal(0, summary.SavedCount);
        Assert.Equal(1, summary.FlaggedCount);
    }

    private static HistoricalLeagueDraftStore NewStore() =>
        new(NullLogger<HistoricalLeagueDraftStore>.Instance, $"historical-image-import-tests-{Guid.NewGuid():N}.json");

    private sealed class NullSleeper : ISleeperLeagueClient
    {
        public Task<SleeperLeagueSnapshot?> GetLeagueSnapshotAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<SleeperLeagueSnapshot?>(null);
        public Task<IReadOnlyList<SleeperDraftSummary>> GetDraftsForLeagueAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SleeperDraftSummary>>([]);
        public Task<SleeperDraftSnapshot?> GetDraftAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<SleeperDraftSnapshot?>(null);
        public Task<IReadOnlyList<SleeperDraftPickSnapshot>> GetDraftPicksAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SleeperDraftPickSnapshot>>([]);
    }
}
