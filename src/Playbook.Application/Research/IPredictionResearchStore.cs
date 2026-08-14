using Playbook.Core.Research;

namespace Playbook.Application.Research;

/// <summary>
/// Permanent research memory for the pre-event snapshot / actual-outcome / post-event-assessment
/// loop. Implementations must never overwrite an existing snapshot or assessment — history is
/// append-only, forever, across preseason/regular season/postseason. Not a cache: no TTL, no
/// eviction.
/// </summary>
public interface IPredictionResearchStore
{
    /// <summary>
    /// Persists a snapshot. No-op (does not overwrite) if a snapshot with the same
    /// <see cref="PredictionSnapshot.SnapshotId"/> already exists.
    /// </summary>
    void SaveSnapshot(PredictionSnapshot snapshot);

    IReadOnlyList<PredictionSnapshot> GetAllSnapshots();

    /// <summary>Snapshots whose event has started (plus a grading buffer) but have no assessment yet.</summary>
    IReadOnlyList<PredictionSnapshot> GetSnapshotsPendingGrading(DateTimeOffset asOf, TimeSpan gradingBuffer);

    /// <summary>
    /// Persists an assessment. No-op (does not overwrite) if an assessment for the same
    /// <see cref="PredictionOutcomeAssessment.SnapshotId"/> already exists.
    /// </summary>
    void SaveAssessment(PredictionOutcomeAssessment assessment);

    IReadOnlyList<PredictionOutcomeAssessment> GetAllAssessments();

    bool HasAssessment(Guid snapshotId);
}
