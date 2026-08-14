using Playbook.Core.Stats.Models;

namespace Playbook.Application.Stats.Interfaces;

/// <summary>
/// Real preseason weekly player box scores, kept entirely separate from
/// <see cref="IPlayerGameLogStore"/> (regular-season history feeding Projection/trend consumers).
/// nflverse's player_stats feed carries only REG/POST rows — it never has preseason data — so
/// preseason actuals come from a different real source (ESPN boxscores) and are used for exactly
/// one purpose: grading preseason Quick Pick research snapshots. Never merged into regular-season
/// projection inputs.
/// </summary>
public interface IPreseasonPlayerGameLogProvider
{
    /// <summary>
    /// Real per-player box-score lines for every completed preseason game on <paramref name="gameDate"/>'s
    /// Eastern calendar day. Returns an empty list (never fabricated data) when no game is found, the
    /// game hasn't finished, or no real source is configured.
    /// </summary>
    Task<IReadOnlyList<PlayerGameStats>> GetPreseasonGameLogsAsync(
        int season,
        DateTimeOffset gameDate,
        CancellationToken cancellationToken = default);
}
