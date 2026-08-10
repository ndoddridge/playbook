using Playbook.Core.Decisions;
using Playbook.Core.Knowledge;
using Playbook.Core.Predictions;

namespace Playbook.Application.Knowledge;

/// <summary>
/// Applies explicit, bounded knowledge-group transforms to prediction inputs.
/// Does not modify Projection V2, Confidence V2, or Decision Policy V1 formulas.
/// </summary>
public interface IKnowledgeImpactApplicator
{
    /// <summary>
    /// Transform decision PlayerKnowledge according to current KnowledgeMode / groups.
    /// Baseline strips contextual knowledge-group inputs; Enhanced applies explicit transforms.
    /// </summary>
    PlayerKnowledge ApplyToPlayerKnowledge(PlayerKnowledge source);

    /// <summary>
    /// Transform a batch (deterministic order preserved).
    /// </summary>
    IReadOnlyList<PlayerKnowledge> ApplyToPlayerKnowledgeBatch(IReadOnlyList<PlayerKnowledge> source);

    /// <summary>
    /// Bounded Quick Picks ranking adjustment from shared knowledge evidence.
    /// Returns the same prediction when Mode is Baseline or no applicable evidence.
    /// </summary>
    Prediction ApplyToQuickPickPrediction(Prediction source, PredictionContext? knowledgeContext);
}
