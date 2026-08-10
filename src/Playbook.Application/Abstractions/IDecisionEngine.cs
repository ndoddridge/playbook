using Playbook.Core.Decisions;
using Playbook.Core.Intelligence.Models;

namespace Playbook.Application.Abstractions;

/// <summary>
/// Central fantasy decision engine. UI features must consume this rather than
/// embedding independent recommendation algorithms.
/// </summary>
public interface IDecisionEngine
{
    /// <summary>
    /// Evaluate a single player in context (absolute decision value).
    /// </summary>
    Task<DecisionResult> EvaluatePlayerAsync(
        PlayerKnowledge knowledge,
        DecisionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Synthesize a decision given knowledge + optional alternatives (comparative).
    /// </summary>
    Task<DecisionResult> EvaluateDecisionAsync(
        PlayerKnowledge knowledge,
        DecisionContext context,
        IReadOnlyList<PlayerKnowledge>? alternatives = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two players for the same decision slot.
    /// </summary>
    Task<(DecisionResult Preferred, DecisionResult Other)> ComparePlayersAsync(
        PlayerKnowledge left,
        PlayerKnowledge right,
        DecisionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Build Start/Sit recommendations for a roster using centralized decision synthesis.
    /// Returns both traceable <see cref="DecisionResult"/> rows and UI-facing Start/Sit models.
    /// </summary>
    Task<StartSitDecisionBatch> EvaluateStartSitAsync(
        IReadOnlyList<PlayerKnowledge> rosterKnowledge,
        IReadOnlyList<StartSitCandidate> candidates,
        DecisionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist a decision result as a structured record (replay-ready shape).
    /// </summary>
    Task<DecisionRecord> RecordDecisionAsync(
        DecisionResult result,
        DecisionContext context,
        CancellationToken cancellationToken = default);
}
