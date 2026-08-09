using System.Collections.Concurrent;
using Playbook.Application.Abstractions;
using Playbook.Core.Decisions;

namespace Playbook.Infrastructure.Decisions;

/// <summary>
/// Process-local decision record store. Shape is replay-ready; persistence can replace this later.
/// </summary>
public sealed class InMemoryDecisionRecordStore : IDecisionRecordStore
{
    private readonly ConcurrentDictionary<Guid, DecisionRecord> _records = new();

    public Task<DecisionRecord> RecordAsync(DecisionRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records[record.DecisionId] = record;
        return Task.FromResult(record);
    }

    public Task<DecisionRecord?> GetAsync(Guid decisionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records.TryGetValue(decisionId, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<DecisionRecord>> ListAsync(
        int? season = null,
        int? week = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<DecisionRecord> query = _records.Values;
        if (season is not null)
        {
            query = query.Where(r => r.Season == season);
        }

        if (week is not null)
        {
            query = query.Where(r => r.Week == week);
        }

        var list = query
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<DecisionRecord>>(list);
    }

    public Task<DecisionRecord?> AttachOutcomeAsync(
        Guid decisionId,
        double actualOutcome,
        string evaluationResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(decisionId, out var existing))
        {
            return Task.FromResult<DecisionRecord?>(null);
        }

        var updated = new DecisionRecord
        {
            DecisionId = existing.DecisionId,
            PlayerId = existing.PlayerId,
            PlayerName = existing.PlayerName,
            DecisionType = existing.DecisionType,
            Recommendation = existing.Recommendation,
            Confidence = existing.Confidence,
            LeagueId = existing.LeagueId,
            SelectedRosterId = existing.SelectedRosterId,
            LeagueName = existing.LeagueName,
            ScoringType = existing.ScoringType,
            Season = existing.Season,
            Week = existing.Week,
            InformationCutoff = existing.InformationCutoff,
            CreatedAt = existing.CreatedAt,
            SupportingEvidence = existing.SupportingEvidence,
            OpposingEvidence = existing.OpposingEvidence,
            Unknowns = existing.Unknowns,
            AlternativesConsidered = existing.AlternativesConsidered,
            ExpectedValue = existing.ExpectedValue,
            DecisionValue = existing.DecisionValue,
            Rationale = existing.Rationale,
            ActualOutcome = actualOutcome,
            EvaluationResult = evaluationResult
        };

        _records[decisionId] = updated;
        return Task.FromResult<DecisionRecord?>(updated);
    }
}
