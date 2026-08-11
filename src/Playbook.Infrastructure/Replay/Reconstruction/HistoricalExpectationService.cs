using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Reconstruction;

/// <summary>
/// Builds features + Baseline A + Projection V1 + Projection V2.
/// Primary decision input selected by <see cref="HistoricalProjectionExperimentState"/> (default V1).
/// </summary>
public sealed class HistoricalExpectationService : IHistoricalExpectationService
{
    private readonly IHistoricalFeatureReconstructor _features;
    private readonly RecentAverageProjectionEngine _baselineA;
    private readonly OpportunityAwareProjectionEngine _projectionV1;
    private readonly CalibratedOpportunityAwareProjectionEngine _projectionV2;
    private readonly PositionSegmentedCalibratedProjectionEngine _projectionV2PositionSegmented;
    private readonly HistoricalProjectionExperimentState _experimentState;

    public HistoricalExpectationService(
        IHistoricalFeatureReconstructor features,
        RecentAverageProjectionEngine baselineA,
        OpportunityAwareProjectionEngine projectionV1,
        CalibratedOpportunityAwareProjectionEngine projectionV2,
        PositionSegmentedCalibratedProjectionEngine projectionV2PositionSegmented,
        HistoricalProjectionExperimentState experimentState)
    {
        _features = features;
        _baselineA = baselineA;
        _projectionV1 = projectionV1;
        _projectionV2 = projectionV2;
        _projectionV2PositionSegmented = projectionV2PositionSegmented;
        _experimentState = experimentState;
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
        var v1 = _projectionV1.Project(reconstructed, scoringType);
        var v2 = _projectionV2.Project(reconstructed, scoringType);

        var primary = _experimentState.PrimaryMode switch
        {
            HistoricalProjectionPrimaryMode.ProjectionV2 =>
                v2.IsValid ? v2 : (v1.IsValid ? v1 : a),
            HistoricalProjectionPrimaryMode.ProjectionV2PositionSegmented =>
                _projectionV2PositionSegmented.Project(reconstructed, scoringType) is { IsValid: true } ps
                    ? ps
                    : (v1.IsValid ? v1 : a),
            _ => v1.IsValid ? v1 : a
        };

        return new HistoricalProjectionBundle
        {
            Features = reconstructed,
            Primary = primary,
            BaselineRecentAverage = a,
            BaselineOpportunityAware = v1,
            ProjectionV2 = v2
        };
    }
}
