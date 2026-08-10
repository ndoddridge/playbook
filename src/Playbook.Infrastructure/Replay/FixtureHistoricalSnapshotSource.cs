using Playbook.Application.Replay;
using Playbook.Core.Leagues;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// v1 historical source backed by the controlled fixture (and future fixture packs).
/// Does not download multi-decade NFL archives.
/// </summary>
public sealed class FixtureHistoricalSnapshotSource : IHistoricalSnapshotSource
{
    public Task<HistoricalRawWeekData?> GetRawWeekAsync(
        int season,
        int week,
        ScoringType scoringType,
        string? fixtureId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var wantsControlled =
            string.IsNullOrWhiteSpace(fixtureId) ||
            string.Equals(fixtureId, ControlledHistoricalFixture.FixtureId, StringComparison.OrdinalIgnoreCase);

        if (wantsControlled &&
            season == ControlledHistoricalFixture.Season &&
            week == ControlledHistoricalFixture.Week)
        {
            return Task.FromResult<HistoricalRawWeekData?>(ControlledHistoricalFixture.Create(scoringType));
        }

        return Task.FromResult<HistoricalRawWeekData?>(null);
    }
}
