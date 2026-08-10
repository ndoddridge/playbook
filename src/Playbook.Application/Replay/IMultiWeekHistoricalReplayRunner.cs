using Playbook.Core.Replay;

namespace Playbook.Application.Replay;

/// <summary>
/// Replays an inclusive historical week range, preserving per-week information cutoffs.
/// Does not rewrite the Decision Engine — each week calls <see cref="IHistoricalReplayRunner"/>.
/// </summary>
public interface IMultiWeekHistoricalReplayRunner
{
    Task<SeasonScorecard> RunAsync(
        MultiWeekReplayRequest request,
        CancellationToken cancellationToken = default);
}
