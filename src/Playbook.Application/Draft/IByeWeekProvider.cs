using Playbook.Core.Draft;

namespace Playbook.Application.Draft;

/// <summary>
/// Bye weeks derived from the real published NFL schedule. Returns
/// <see cref="ByeWeekMap.Empty"/> when the schedule is unavailable or incomplete, so callers
/// degrade to "bye data unavailable" rather than guessing.
/// </summary>
public interface IByeWeekProvider
{
    ByeWeekMap GetByeWeeks(int season);

    Task RefreshAsync(int season, CancellationToken cancellationToken = default);
}
