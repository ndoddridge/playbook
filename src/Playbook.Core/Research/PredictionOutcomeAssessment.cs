namespace Playbook.Core.Research;

/// <summary>
/// ACTUAL_OUTCOME + POST_EVENT_ASSESSMENT for one <see cref="PredictionSnapshot"/>. Written once,
/// after the event, and never overwritten — a second grading attempt for the same snapshot is a
/// no-op at the store level. Answers "what actually happened, and why was the snapshot right or
/// wrong."
/// </summary>
public sealed class PredictionOutcomeAssessment
{
    /// <summary>Links back to <see cref="PredictionSnapshot.SnapshotId"/>.</summary>
    public required Guid SnapshotId { get; init; }

    /// <summary>Actual stat value for the snapshot's market, in market units. Null when ungraded (DataGap).</summary>
    public required decimal? ActualValue { get; init; }

    /// <summary>Whether the snapshot's picked direction (Over/Under/Yes/No) actually hit. Null when ungraded.</summary>
    public required bool? DirectionHit { get; init; }

    /// <summary>ActualValue minus PlaybookProjection, in market units. Null when either side is unknown.</summary>
    public required decimal? ProjectionDelta { get; init; }

    public required PredictionOutcomeClassification Classification { get; init; }

    public required string AssessmentNotes { get; init; }

    public required DateTimeOffset GradedAt { get; init; }
}
