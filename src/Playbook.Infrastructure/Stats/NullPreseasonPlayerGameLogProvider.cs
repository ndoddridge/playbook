using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Used when live stats are not configured (Mock mode). Never fabricates preseason box scores —
/// simply reports none available.
/// </summary>
public sealed class NullPreseasonPlayerGameLogProvider : IPreseasonPlayerGameLogProvider
{
    public Task<IReadOnlyList<PlayerGameStats>> GetPreseasonGameLogsAsync(
        int season,
        DateTimeOffset gameDate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlayerGameStats>>([]);
}
