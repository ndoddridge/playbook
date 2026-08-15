using Playbook.Core.Predictions.Models;

namespace Playbook.Application.Predictions.Interfaces;

/// <summary>
/// Real completed NFL final scores. The only source of truth for team points in Playbook.
/// Implementations must never surface sportsbook lines through this interface.
/// </summary>
public interface IHistoricalGameScoreProvider
{
    /// <summary>
    /// Completed regular-season games with final scores, for the requested season and any
    /// seasons needed as priors. Empty when unavailable — callers must degrade to NO PLAY.
    /// </summary>
    IReadOnlyList<HistoricalGameScore> GetCompletedGames(int season);

    /// <summary>True once real score data has been loaded at least once.</summary>
    bool IsLoaded { get; }

    Task RefreshAsync(int season, CancellationToken cancellationToken = default);
}
