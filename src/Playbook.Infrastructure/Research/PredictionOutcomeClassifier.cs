using Playbook.Application.Research;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Predictions;
using Playbook.Core.Research;

namespace Playbook.Infrastructure.Research;

/// <summary>
/// Deterministic, threshold-based grading. Bands are intentionally coarse — this is evidence
/// collection, not a scored model, and the available signals (no snap share / depth chart) can't
/// support finer distinctions. See docs for the "Remaining data limitations" this implies:
/// RoleError and MeaningfulRoleSignal are inferred from production magnitude alone, not from a
/// verified role change.
/// </summary>
public sealed class PredictionOutcomeClassifier : IPredictionOutcomeClassifier
{
    private const decimal PreseasonNoiseTolerance = 0.35m;
    private const decimal RegularSeasonNoiseTolerance = 0.20m;
    private const decimal PreseasonBigMissThreshold = 0.70m;
    private const decimal RegularSeasonBigMissThreshold = 0.50m;

    public PredictionOutcomeAssessment Classify(
        PredictionSnapshot snapshot,
        decimal? actualValue,
        PlayerInjuryRecord? injuryAtGradingTime)
    {
        var now = DateTimeOffset.UtcNow;

        if (actualValue is null)
        {
            return new PredictionOutcomeAssessment
            {
                SnapshotId = snapshot.SnapshotId,
                ActualValue = null,
                DirectionHit = null,
                ProjectionDelta = null,
                Classification = PredictionOutcomeClassification.DataGap,
                AssessmentNotes = "No actual stat could be retrieved for this player/market/week.",
                GradedAt = now
            };
        }

        var directionHit = ComputeDirectionHit(snapshot, actualValue.Value);
        var projectionDelta = snapshot.PlaybookProjection is { } proj ? actualValue.Value - proj : (decimal?)null;
        var isPreseason = snapshot.SeasonPhase == NflSeasonPhase.Preseason;
        var noiseTolerance = isPreseason ? PreseasonNoiseTolerance : RegularSeasonNoiseTolerance;
        var bigMissThreshold = isPreseason ? PreseasonBigMissThreshold : RegularSeasonBigMissThreshold;

        if (projectionDelta is null)
        {
            var classification = directionHit == true
                ? PredictionOutcomeClassification.Success
                : isPreseason ? PredictionOutcomeClassification.PreseasonNoise : PredictionOutcomeClassification.RegularSeasonNoise;
            return new PredictionOutcomeAssessment
            {
                SnapshotId = snapshot.SnapshotId,
                ActualValue = actualValue,
                DirectionHit = directionHit,
                ProjectionDelta = null,
                Classification = classification,
                AssessmentNotes = "No PlaybookProjection on file at snapshot time — graded on direction only.",
                GradedAt = now
            };
        }

        var relativeDelta = projectionDelta.Value / Math.Max(Math.Abs(snapshot.PlaybookProjection!.Value), 1m);
        var absRelativeDelta = Math.Abs(relativeDelta);

        if (directionHit != false && absRelativeDelta <= noiseTolerance)
        {
            return Build(
                snapshot, actualValue, directionHit, projectionDelta,
                PredictionOutcomeClassification.Success,
                $"Actual {actualValue:0.##} vs projection {snapshot.PlaybookProjection:0.##} — within tolerance.",
                now);
        }

        if (injuryAtGradingTime is { Severity: InjurySeverity.Significant or InjurySeverity.Major })
        {
            return Build(
                snapshot, actualValue, directionHit, projectionDelta,
                PredictionOutcomeClassification.InjurySignal,
                $"Miss coincides with a {injuryAtGradingTime.Severity} injury on file ({injuryAtGradingTime.Status}).",
                now);
        }

        if (relativeDelta >= bigMissThreshold)
        {
            return Build(
                snapshot, actualValue, directionHit, projectionDelta,
                PredictionOutcomeClassification.MeaningfulRoleSignal,
                $"Actual {actualValue:0.##} far exceeded projection {snapshot.PlaybookProjection:0.##} — possible role change worth tracking.",
                now);
        }

        if (relativeDelta <= -bigMissThreshold)
        {
            return Build(
                snapshot, actualValue, directionHit, projectionDelta,
                PredictionOutcomeClassification.RoleError,
                $"Actual {actualValue:0.##} far below projection {snapshot.PlaybookProjection:0.##} with no injury on file — role likely differed from the snapshot's assumption.",
                now);
        }

        if (isPreseason)
        {
            return Build(
                snapshot, actualValue, directionHit, projectionDelta,
                PredictionOutcomeClassification.PreseasonNoise,
                "Miss within the elevated preseason variance band.",
                now);
        }

        if (absRelativeDelta <= RegularSeasonNoiseTolerance * 1.75m)
        {
            return Build(
                snapshot, actualValue, directionHit, projectionDelta,
                PredictionOutcomeClassification.RegularSeasonNoise,
                "Miss within normal week-to-week variance.",
                now);
        }

        return Build(
            snapshot, actualValue, directionHit, projectionDelta,
            PredictionOutcomeClassification.ProjectionError,
            $"Meaningful miss (actual {actualValue:0.##} vs projection {snapshot.PlaybookProjection:0.##}) with no injury or magnitude signal to explain it.",
            now);
    }

    private static PredictionOutcomeAssessment Build(
        PredictionSnapshot snapshot,
        decimal? actualValue,
        bool? directionHit,
        decimal? projectionDelta,
        PredictionOutcomeClassification classification,
        string notes,
        DateTimeOffset now) => new()
    {
        SnapshotId = snapshot.SnapshotId,
        ActualValue = actualValue,
        DirectionHit = directionHit,
        ProjectionDelta = projectionDelta,
        Classification = classification,
        AssessmentNotes = notes,
        GradedAt = now
    };

    private static bool? ComputeDirectionHit(PredictionSnapshot snapshot, decimal actualValue) =>
        snapshot.Direction switch
        {
            PredictionDirection.Over when snapshot.Line is { } line => actualValue > line,
            PredictionDirection.Under when snapshot.Line is { } line => actualValue < line,
            PredictionDirection.Yes => actualValue >= 1m,
            PredictionDirection.No => actualValue < 1m,
            _ => null
        };
}
