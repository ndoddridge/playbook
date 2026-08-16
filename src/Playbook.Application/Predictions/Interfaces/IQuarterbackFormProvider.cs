using Playbook.Core.Predictions.Models;

namespace Playbook.Application.Predictions.Interfaces;

/// <summary>
/// Completed-game quarterback passing lines, used to measure QB quality heading into a game.
///
/// Returns raw per-game lines rather than a pre-aggregated rating so the caller — and its tests —
/// control the chronological cutoff. A provider that pre-aggregated would make leakage invisible.
/// </summary>
public interface IQuarterbackFormProvider
{
    /// <summary>
    /// Quarterback game lines for the given season. Empty when unavailable, which makes the
    /// team-points model fall back to its baseline coefficients rather than guessing.
    /// </summary>
    IReadOnlyList<QuarterbackGameLine> GetQuarterbackLines(int season);

    bool IsLoaded { get; }

    Task RefreshAsync(int season, CancellationToken cancellationToken = default);
}
