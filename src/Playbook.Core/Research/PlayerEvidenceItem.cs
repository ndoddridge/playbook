using Playbook.Core.Predictions;

namespace Playbook.Core.Research;

/// <summary>
/// One piece of structured, attributable evidence derived from a single graded Quick Pick
/// research-memory snapshot — "what did Playbook believe before this game, what actually
/// happened, and how much should that matter." Never a projection, score, or instruction to
/// change one; it is purely descriptive evidence for a downstream consumer to weigh alongside
/// everything else it already knows. <see cref="Weight"/> already folds in classification
/// reliability, season-phase discount, and recency decay, so a single noisy preseason observation
/// can never carry the same evidentiary force as a confirmed regular-season pattern.
/// </summary>
public sealed class PlayerEvidenceItem
{
    /// <summary>Links back to the originating immutable <c>PredictionSnapshot</c>.</summary>
    public required Guid SnapshotId { get; init; }

    public required Guid PlayerId { get; init; }

    public required string? PlayerName { get; init; }

    public required EvidenceType Type { get; init; }

    public required NflSeasonPhase Phase { get; init; }

    public required int Season { get; init; }

    public required int Week { get; init; }

    public required PredictionMarketType Market { get; init; }

    /// <summary>Human-readable, e.g. "Preseason Wk 1: ReceivingYards actual 62 vs projection 45 — MeaningfulRoleChange."</summary>
    public required string Summary { get; init; }

    /// <summary>
    /// 0–1 evidentiary weight: classification reliability × season-phase discount × recency decay.
    /// Recomputed fresh every call (never persisted), so it naturally fades as the item ages and
    /// naturally normalizes once the real season moves from preseason into regular season — no
    /// code change needed for that transition.
    /// </summary>
    public required double Weight { get; init; }

    /// <summary>When the outcome was graded (<c>PredictionOutcomeAssessment.GradedAt</c>).</summary>
    public required DateTimeOffset ObservedAt { get; init; }

    public required string Source { get; init; }
}
