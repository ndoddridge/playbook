using Playbook.Core.Players;
using Playbook.Infrastructure.Replay.Calibration;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Per-DI-container switch holding the frozen per-position-group calibration used only while
/// <see cref="HistoricalProjectionExperimentState.PrimaryMode"/> is
/// <c>ProjectionV2PositionSegmented</c>. Set/cleared exclusively by
/// <c>PositionSegmentedCalibrationExperimentRunner</c>; never populated in production.
/// </summary>
public sealed class PositionSegmentedCalibrationState
{
    /// <summary>Null outside an active position-segmented-calibration-v1 experiment run.</summary>
    public IReadOnlyDictionary<Position, ProjectionCalibrationFitter.FittedCalibration>? Active { get; set; }
}
