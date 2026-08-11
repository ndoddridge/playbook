using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay.Calibration;

namespace Playbook.Infrastructure.Replay.Reconstruction;

/// <summary>
/// Position-Segmented Projection Calibration V1 (experiment): applies a per-position-group
/// piecewise calibration to Projection V1 (opportunity-aware) outputs, instead of the single
/// global <see cref="FrozenProjectionCalibrationV2"/> curve. Does not modify V1 formulas.
/// Confidence is intentionally unchanged, same as Projection V2.
///
/// Only active while <see cref="PositionSegmentedCalibrationState.Active"/> is configured by
/// <c>PositionSegmentedCalibrationExperimentRunner</c>. If no group calibration is configured
/// for a position (including outside the experiment, or K/DST which are not part of any fitted
/// group), this falls back to the global frozen V2 calibration rather than raw V1 — a safe
/// default that never regresses below the already-accepted V2 baseline.
/// </summary>
public sealed class PositionSegmentedCalibratedProjectionEngine : IHistoricalProjectionEngine
{
    public const string Id = "projection-calibrated-v2-position-segmented-v1";
    public const string Label = "Projection V2 · Position-segmented calibration (experiment)";

    private readonly OpportunityAwareProjectionEngine _v1;
    private readonly PositionSegmentedCalibrationState _state;

    public PositionSegmentedCalibratedProjectionEngine(
        OpportunityAwareProjectionEngine v1,
        PositionSegmentedCalibrationState state)
    {
        _v1 = v1;
        _state = state;
    }

    public string ModelId => Id;

    public string ModelLabel => Label;

    public HistoricalProjection Project(HistoricalPlayerFeatures features, ScoringType scoringType)
    {
        var v1 = _v1.Project(features, scoringType);
        if (!v1.IsValid)
        {
            return new HistoricalProjection
            {
                ModelId = ModelId,
                ModelLabel = ModelLabel,
                ProjectedPoints = 0,
                Floor = null,
                Ceiling = null,
                ProjectionConfidence = v1.ProjectionConfidence,
                Sufficiency = v1.Sufficiency,
                SourceWeeks = v1.SourceWeeks,
                Methodology = "Position-segmented V2 invalid because V1 had insufficient history."
            };
        }

        var groupFit = _state.Active is not null && _state.Active.TryGetValue(features.Position, out var fit)
            ? fit
            : (ProjectionCalibrationFitter.FittedCalibration?)null;

        double calibrated;
        string groupNote;
        if (groupFit is { } f)
        {
            calibrated = ProjectionCalibrationFitter.Apply(f, v1.ProjectedPoints);
            groupNote = $"group fit method={f.Method} low=({f.LowIntercept:0.####},{f.LowSlope:0.####}) " +
                        $"high=({f.HighIntercept:0.####},{f.HighSlope:0.####})";
        }
        else
        {
            calibrated = FrozenProjectionCalibrationV2.Apply(v1.ProjectedPoints);
            groupNote = "no group calibration configured for this position — fell back to global Projection V2";
        }

        var scale = v1.ProjectedPoints <= 1e-9 ? 1.0 : calibrated / v1.ProjectedPoints;
        double? floor = v1.Floor is null ? null : Math.Round(Math.Max(0, v1.Floor.Value * scale), 1, MidpointRounding.AwayFromZero);
        double? ceiling = v1.Ceiling is null ? null : Math.Round(Math.Max(0, v1.Ceiling.Value * scale), 1, MidpointRounding.AwayFromZero);

        return new HistoricalProjection
        {
            ModelId = ModelId,
            ModelLabel = ModelLabel,
            ProjectedPoints = calibrated,
            Floor = floor,
            Ceiling = ceiling,
            ProjectionConfidence = v1.ProjectionConfidence,
            Sufficiency = v1.Sufficiency,
            SourceWeeks = v1.SourceWeeks,
            Methodology =
                $"Position-segmented V2 calibration ({features.Position}) applied to V1={v1.ProjectedPoints:0.0} " +
                $"→ {calibrated:0.0}. {groupNote}. Underlying: {v1.Methodology}"
        };
    }
}
