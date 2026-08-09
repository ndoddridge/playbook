using Playbook.Application.Predictions;
using Playbook.Core.Decisions;
using Playbook.Core.Knowledge;
using Playbook.Core.Replay;

namespace Playbook.Application.Knowledge;

/// <summary>
/// Shared football intelligence substrate.
/// Produces structured knowledge and PredictionContext for any prediction type.
/// Does not decide Start/Sit, Quick Picks, or other outcomes.
/// </summary>
public interface ISharedKnowledgeModel
{
    /// <summary>
    /// Reconstruct knowledge for one player from a cutoff-safe historical snapshot.
    /// </summary>
    SharedKnowledgeBundle BuildFromHistorical(
        HistoricalSnapshot snapshot,
        Guid playerId,
        PredictionType predictionType);

    /// <summary>
    /// Build PredictionContext for a historical player/situation.
    /// </summary>
    PredictionContext BuildHistoricalPredictionContext(
        HistoricalSnapshot snapshot,
        Guid playerId,
        PredictionType predictionType,
        DecisionContext? decisionContext = null);

    /// <summary>
    /// Wrap existing decision PlayerKnowledge into the shared bundle (live Start/Sit path).
    /// </summary>
    SharedKnowledgeBundle BuildFromPlayerKnowledge(
        PlayerKnowledge knowledge,
        PredictionType predictionType,
        int season,
        int week,
        string? team = null,
        string? opponentTeam = null);

    /// <summary>
    /// Build PredictionContext for live fantasy Start/Sit from composed PlayerKnowledge.
    /// </summary>
    PredictionContext BuildStartSitPredictionContext(
        PlayerKnowledge knowledge,
        DecisionContext decisionContext,
        string? team = null,
        string? opponentTeam = null);

    /// <summary>
    /// Build shared knowledge + PredictionContext for a Quick Pick evaluation.
    /// Does not alter Quick Pick scoring — attaches intelligence substrate only.
    /// </summary>
    PredictionContext BuildQuickPickPredictionContext(
        QuickPickEvaluationContext evaluation,
        DateTimeOffset? informationCutoff = null);
}
