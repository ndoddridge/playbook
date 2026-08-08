using Playbook.Core.Stats.Models;

namespace Playbook.Application.Stats.Interfaces;

/// <summary>
/// Application facade for player statistics. Backed by providers + local cache.
/// </summary>
public interface IPlayerStatsService
{
    IReadOnlyList<PlayerSeasonStats> GetAllStats();

    IReadOnlyList<PlayerSeasonStats> GetStatsForPlayer(Guid playerId);

    IReadOnlyList<int> GetAvailableSeasons(Guid playerId);

    PlayerSeasonStats? GetStats(Guid playerId, int season, StatsPeriod? period = null);

    /// <summary>Best NFL season record for projection baselines (current if usable, else latest completed).</summary>
    PlayerSeasonStats? GetPrimaryProductionSeason(Guid playerId);

    IReadOnlyList<PlayerSeasonStats> GetRecentNflSeasons(Guid playerId, int maxSeasons = 3);

    void Refresh();

    Task RefreshAsync(CancellationToken cancellationToken = default);
}