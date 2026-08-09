using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Reconstruction;

/// <summary>
/// Baseline A: simple recent fantasy-point average (last 3 when available, else all prior games).
/// Transparent and intentionally "dumb" for scientific comparison.
/// </summary>
public sealed class RecentAverageProjectionEngine : IHistoricalProjectionEngine
{
    public const string Id = "baseline-recent-average-v1";

    public string ModelId => Id;

    public string ModelLabel => "Baseline A · Recent average";

    public HistoricalProjection Project(HistoricalPlayerFeatures features, ScoringType scoringType)
    {
        _ = scoringType;

        if (features.Sufficiency == DataSufficiency.Insufficient ||
            features.FantasyPointsPerGame is null)
        {
            return Invalid(features, "No prior games available before cutoff.");
        }

        var projected = features.Last3FantasyPointsAvg ?? features.FantasyPointsPerGame.Value;
        var floor = Math.Max(0, projected * 0.55);
        var ceiling = projected * 1.45;
        var confidence = features.Sufficiency switch
        {
            DataSufficiency.Sufficient => Math.Clamp(features.FeatureConfidence - 5, 40, 80),
            DataSufficiency.Limited => Math.Clamp(features.FeatureConfidence - 8, 20, 50),
            _ => 12
        };

        return new HistoricalProjection
        {
            ModelId = ModelId,
            ModelLabel = ModelLabel,
            ProjectedPoints = Math.Round(projected, 1, MidpointRounding.AwayFromZero),
            Floor = Math.Round(floor, 1, MidpointRounding.AwayFromZero),
            Ceiling = Math.Round(ceiling, 1, MidpointRounding.AwayFromZero),
            ProjectionConfidence = confidence,
            Sufficiency = features.Sufficiency,
            SourceWeeks = features.SourceWeeks,
            Methodology =
                $"Mean of last {Math.Min(3, features.GamesUsed)} prior game(s) fantasy points " +
                $"(weeks {string.Join(',', features.SourceWeeks.TakeLast(Math.Min(3, features.SourceWeeks.Count)))}; " +
                $"never includes week {features.TargetWeek}). Scoring context: {scoringType}."
        };
    }

    private HistoricalProjection Invalid(HistoricalPlayerFeatures features, string reason) =>
        new()
        {
            ModelId = ModelId,
            ModelLabel = ModelLabel,
            ProjectedPoints = 0,
            Floor = null,
            Ceiling = null,
            ProjectionConfidence = 10,
            Sufficiency = DataSufficiency.Insufficient,
            SourceWeeks = features.SourceWeeks,
            Methodology = reason
        };
}
