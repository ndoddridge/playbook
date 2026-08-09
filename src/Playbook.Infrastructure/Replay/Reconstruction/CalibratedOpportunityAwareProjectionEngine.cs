using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Reconstruction;

/// <summary>
/// Projection V2: applies frozen calibration to Projection V1 (opportunity-aware) outputs.
/// Does not modify V1 formulas. Confidence is intentionally unchanged (Experiment 2).
/// </summary>
public sealed class CalibratedOpportunityAwareProjectionEngine : IHistoricalProjectionEngine
{
    private readonly OpportunityAwareProjectionEngine _v1;

    public CalibratedOpportunityAwareProjectionEngine(OpportunityAwareProjectionEngine v1)
    {
        _v1 = v1;
    }

    public string ModelId => FrozenProjectionCalibrationV2.ModelId;

    public string ModelLabel => FrozenProjectionCalibrationV2.ModelLabel;

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
                Methodology = "V2 invalid because V1 had insufficient history."
            };
        }

        var calibrated = FrozenProjectionCalibrationV2.Apply(v1.ProjectedPoints);
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
            // Intentionally unchanged — confidence calibration is a separate experiment.
            ProjectionConfidence = v1.ProjectionConfidence,
            Sufficiency = v1.Sufficiency,
            SourceWeeks = v1.SourceWeeks,
            Methodology =
                $"V2 calibration ({FrozenProjectionCalibrationV2.Method}) applied to V1={v1.ProjectedPoints:0.0} " +
                $"→ {calibrated:0.0}. {FrozenProjectionCalibrationV2.SelectionSummary} " +
                $"Underlying: {v1.Methodology}"
        };
    }
}
