using Playbook.Core.Draft;
using Playbook.Core.Historical;
using Playbook.Core.Leagues;

namespace Playbook.Application.Historical;

public interface IHistoricalLeagueIntelligenceService
{
    Task<HistoricalImportResult> ImportSleeperLeagueHistoryAsync(string leagueId, CancellationToken cancellationToken = default);
    /// <summary>Imports a single completed Sleeper draft by pasted URL or id, without requiring a connected league. Rejects drafts that aren't complete yet.</summary>
    Task<HistoricalImportResult> ImportSleeperDraftByIdAsync(string draftUrlOrId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Draft Assistant personal-learning import. Requires a selected league AND team; otherwise the
    /// import is rejected and nothing is persisted. On success, the draft is imported through the
    /// existing historical pipeline and player-vs-player evidence is stored under that LeagueId + TeamId.
    /// </summary>
    Task<HistoricalImportResult> ImportSleeperDraftForPersonalLearningAsync(
        string draftUrlOrId, PersonalDraftLearningRequest? scope, CancellationToken cancellationToken = default);
    /// <summary>
    /// Resolves parsed screenshot picks to player/owner identities and, when
    /// <see cref="DraftImportContext.IsCompleteDraft"/> is true, imports and persists the result.
    /// For an in-progress mock it returns the resolved picks without persisting anything.
    /// </summary>
    Task<DraftImportSummary> ImportFromImageAsync(DraftImageParseResult parsed, DraftImportContext context, CancellationToken cancellationToken = default);
    /// <summary>
    /// Screenshot import for Draft Assistant personal learning. Requires a selected league AND team.
    /// </summary>
    Task<DraftImportSummary> ImportFromImageForPersonalLearningAsync(
        DraftImageParseResult parsed, DraftImportContext context, PersonalDraftLearningRequest? scope, CancellationToken cancellationToken = default);
    Task<HistoricalImportResult> ImportJsonAsync(string json, CancellationToken cancellationToken = default);
    Task<HistoricalImportResult> ImportAsync(HistoricalLeagueDraft draft, CancellationToken cancellationToken = default);
    Task<HistoricalImportResult> ImportAdpSnapshotAsync(HistoricalAdpSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<HistoricalImportResult> ImportAdpSnapshotJsonAsync(string json, CancellationToken cancellationToken = default);
    IReadOnlyList<HistoricalLeagueDraft> GetDrafts(string? leagueId = null, LeagueType? leagueType = null);
    IReadOnlyList<HistoricalOwnerTendency> GetOwnerTendencies(string leagueId, LeagueType leagueType = LeagueType.Redraft);
    HistoricalPlayerHistory GetPlayerHistory(string leagueId, string playerKey, LeagueType leagueType = LeagueType.Redraft);
    IReadOnlyList<HistoricalPositionDraftRange> GetPositionDraftRanges(string leagueId, LeagueType leagueType = LeagueType.Redraft);
    IReadOnlyList<HistoricalLeaguePlayerRange> GetLeaguePlayerRanges(string leagueId, LeagueType leagueType = LeagueType.Redraft);
    IReadOnlyList<HistoricalLeaguePositionTendency> GetLeaguePositionTendencies(string leagueId, LeagueType leagueType = LeagueType.Redraft);
    IReadOnlyList<HistoricalOwnerMarketSignal> GetOwnerMarketSignals(string leagueId, LeagueType leagueType = LeagueType.Redraft);
    IReadOnlyList<HistoricalPickMarketComparison> GetPickMarketComparisons(string leagueId, LeagueType leagueType = LeagueType.Redraft);
    HistoricalTargetPlayerQuery QueryTargetPlayer(string leagueId, string playerKey, int currentPick, int nextPick, LeagueType leagueType = LeagueType.Redraft);
    /// <summary>Personal knowledge for exactly this LeagueId + TeamId. Null when none exists. Never returns another scope.</summary>
    PersonalDraftKnowledge? GetPersonalKnowledge(string leagueId, string teamId);
    /// <summary>Learn from an already-imported/reconstructed draft for the selected league + team. No-op when the owner cannot be identified.</summary>
    PersonalDraftKnowledge? LearnFromImportedDraft(HistoricalLeagueDraft draft, PersonalDraftLearningRequest? scope);
}
