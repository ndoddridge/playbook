using Playbook.Core.Stats.Models;

namespace Playbook.Application.Stats.Interfaces;

/// <summary>
/// Application facade for player statistics. Backed by providers + local store/cache.
/// UI and Intelligence consume this normalized layer — never provider DTOs.
/// </summary>
public interface IPlayerStatsService : IPlayerGameLogStore
{
    IReadOnlyList<PlayerSeasonStats> GetAllStats();

    IReadOnlyList<PlayerSeasonStats> GetStatsForPlayer(Guid playerId);

    IReadOnlyList<int> GetAvailableSeasons(Guid playerId);

    PlayerSeasonStats? GetStats(Guid playerId, int season, StatsPeriod? period = null);

    PlayerSeasonStats? GetCareerTotals(Guid playerId);

    /// <summary>Best NFL season record for projection baselines (current if usable, else latest completed).</summary>
    PlayerSeasonStats? GetPrimaryProductionSeason(Guid playerId);

    IReadOnlyList<PlayerSeasonStats> GetRecentNflSeasons(Guid playerId, int maxSeasons = 3);

    void Refresh();

    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Incremental current-season update without re-downloading all historical seasons.</summary>
    Task RefreshCurrentSeasonAsync(CancellationToken cancellationToken = default);
}
