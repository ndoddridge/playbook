using Playbook.Core.Stats.Models;

namespace Playbook.Application.Stats.Interfaces;

/// <summary>
/// Retains granular weekly/game statistics for trend and intelligence consumers.
/// </summary>
public interface IPlayerGameLogStore
{
    IReadOnlyList<PlayerGameStats> GetAllGameLogs();

    IReadOnlyList<PlayerGameStats> GetGameLogsForPlayer(Guid playerId);

    IReadOnlyList<PlayerGameStats> GetRecentGameLogs(Guid playerId, int maxGames = 8);

    int GameLogCount { get; }
}
