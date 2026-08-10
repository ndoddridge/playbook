using Playbook.Core.Replay;

namespace Playbook.Application.Replay;

/// <summary>
/// Builds a cutoff-safe <see cref="HistoricalSnapshot"/> and separately exposes week outcomes.
/// Outcomes are never embedded in the snapshot.
/// </summary>
public interface IHistoricalSnapshotBuilder
{
    /// <summary>
    /// Filters raw data to observations at or before the information cutoff.
    /// Future-dated injuries/news/projections are dropped and marked unavailable when stripped.
    /// </summary>
    (HistoricalSnapshot Snapshot, HistoricalWeekOutcomes Outcomes) Build(HistoricalRawWeekData raw);
}
