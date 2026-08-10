using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Reconstruction;

/// <summary>
/// Builds features + Baseline A/B projections. Primary decision input is Baseline B when valid.
/// </summary>
public sealed class HistoricalExpectationService : IHistoricalExpectationService
{
    private readonly IHistoricalFeatureReconstructor _features;
    private readonly RecentAverageProjectionEngine _baselineA;
    private readonly OpportunityAwareProjectionEngine _baselineB;

    public HistoricalExpectationService(
        IHistoricalFeatureReconstructor features,
        RecentAverageProjectionEngine baselineA,
        OpportunityAwareProjectionEngine baselineB)
    {
        _features = features;
        _baselineA = baselineA;
        _baselineB = baselineB;
    }

    public HistoricalProjectionBundle BuildExpectations(
        Guid playerId,
        string playerName,
        Position position,
        string team,
        int season,
        int targetWeek,
        DateTimeOffset informationCutoff,
        IReadOnlyList<HistoricalGameObservation> priorGames,
        ScoringType scoringType,
        string? roleNote = null)
    {
        // Hard temporal assertion for regression safety.
        if (priorGames.Any(g => g.Season > season || (g.Season == season && g.Week >= targetWeek)))
        {
            throw new InvalidOperationException(
                $"Future/target-week observations passed to expectation service for {playerName} " +
                $"(season {season} week {targetWeek}).");
        }

        var reconstructed = _features.Reconstruct(
            playerId,
            playerName,
            position,
            team,
            season,
            targetWeek,
            informationCutoff,
            priorGames,
            roleNote);

        var a = _baselineA.Project(reconstructed, scoringType);
        var b = _baselineB.Project(reconstructed, scoringType);
        var primary = b.IsValid ? b : a;

        return new HistoricalProjectionBundle
        {
            Features = reconstructed,
            Primary = primary,
            BaselineRecentAverage = a,
            BaselineOpportunityAware = b
        };
    }
}
