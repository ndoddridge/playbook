namespace Playbook.Application.Replay;

/// <summary>
/// Resolves regular-season week bounds for a historical season without hardcoding 17/18.
/// </summary>
public interface IHistoricalSeasonCalendar
{
    /// <summary>Returns the maximum REG week for the season (typically 17 or 18).</summary>
    Task<int> GetRegularSeasonEndWeekAsync(int season, CancellationToken cancellationToken = default);
}
