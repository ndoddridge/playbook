using Playbook.Core.Decisions;

namespace Playbook.Application.Abstractions;

/// <summary>
/// Persistence surface for decision records (in-memory for v1; replay-ready later).
/// </summary>
public interface IDecisionRecordStore
{
    Task<DecisionRecord> RecordAsync(DecisionRecord record, CancellationToken cancellationToken = default);

    Task<DecisionRecord?> GetAsync(Guid decisionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DecisionRecord>> ListAsync(
        int? season = null,
        int? week = null,
        CancellationToken cancellationToken = default);
}
