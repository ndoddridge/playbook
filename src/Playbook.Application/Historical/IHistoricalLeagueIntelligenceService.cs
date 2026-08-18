using Playbook.Core.Historical;
using Playbook.Core.Leagues;

namespace Playbook.Application.Historical;

public interface IHistoricalLeagueIntelligenceService
{
    Task<HistoricalImportResult> ImportSleeperLeagueHistoryAsync(string leagueId, CancellationToken cancellationToken = default);
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
}
