using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay.Nflverse;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Routes historical week loads to the synthetic leakage fixture or real nflverse provider.
/// Blank fixtureId prefers real data when the provider supports the season/week.
/// </summary>
public sealed class CompositeHistoricalSnapshotSource : IHistoricalSnapshotSource
{
    private readonly IEnumerable<IHistoricalDataProvider> _providers;

    public CompositeHistoricalSnapshotSource(IEnumerable<IHistoricalDataProvider> providers)
    {
        _providers = providers;
    }

    public Task<HistoricalRawWeekData?> GetRawWeekAsync(
        int season,
        int week,
        ScoringType scoringType,
        string? fixtureId = null,
        CancellationToken cancellationToken = default) =>
        GetRawWeekAsync(
            season,
            week,
            scoringType,
            fixtureId,
            HistoricalCandidateUniverse.LabRoster,
            cancellationToken);

    public async Task<HistoricalRawWeekData?> GetRawWeekAsync(
        int season,
        int week,
        ScoringType scoringType,
        string? fixtureId,
        HistoricalCandidateUniverse candidateUniverse,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Explicit synthetic fixture — never replace this path.
        if (string.Equals(fixtureId, ControlledHistoricalFixture.FixtureId, StringComparison.OrdinalIgnoreCase))
        {
            if (season == ControlledHistoricalFixture.Season && week == ControlledHistoricalFixture.Week)
            {
                return ControlledHistoricalFixture.Create(scoringType);
            }

            return null;
        }

        // Optional explicit provider routing: "nflverse" or "nflverse-2018-w7"
        var wantsNflverse =
            string.IsNullOrWhiteSpace(fixtureId) ||
            fixtureId.StartsWith(NflverseHistoricalDataProvider.ProviderKey, StringComparison.OrdinalIgnoreCase);

        if (!wantsNflverse)
        {
            return null;
        }

        foreach (var provider in _providers)
        {
            if (!provider.Supports(season, week))
            {
                continue;
            }

            var weekData = await provider
                .GetWeekAsync(season, week, scoringType, candidateUniverse, cancellationToken)
                .ConfigureAwait(false);
            if (weekData is not null)
            {
                return weekData;
            }
        }

        return null;
    }
}
