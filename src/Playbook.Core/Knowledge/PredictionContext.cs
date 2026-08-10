using Playbook.Core.Decisions;
using Playbook.Core.Leagues;
using Playbook.Core.Players;

namespace Playbook.Core.Knowledge;

/// <summary>
/// Reusable prediction context assembled from shared knowledge.
/// Consumed by Start/Sit, Quick Picks, and future prediction types.
/// Does not itself decide Start/Sit or pick winners.
/// </summary>
public sealed class PredictionContext
{
    public required PredictionType PredictionType { get; init; }

    public required int Season { get; init; }

    public required int Week { get; init; }

    public DateTimeOffset? InformationCutoff { get; init; }

    public Guid? PlayerId { get; init; }

    public string? PlayerName { get; init; }

    public Position? Position { get; init; }

    public string? Team { get; init; }

    public string? OpponentTeam { get; init; }

    public ScoringType? ScoringType { get; init; }

    public Guid? LeagueId { get; init; }

    public required SharedKnowledgeBundle Knowledge { get; init; }

    /// <summary>Optional projection summary available at context build time.</summary>
    public decimal? ProjectedPoints { get; init; }

    public int? ProjectionConfidence { get; init; }

    /// <summary>Optional market line for over/under style predictions.</summary>
    public decimal? MarketLine { get; init; }

    public string? MarketLabel { get; init; }

    /// <summary>
    /// Nearest-peer projection/ranking gap in prediction units (fantasy points or counting-stat units).
    /// Used by margin-gated knowledge transforms. Null when no peer exists.
    /// </summary>
    public double? ComparativeMargin { get; init; }

    /// <summary>Decision-context stamp when fantasy decisions are in scope.</summary>
    public DecisionContext? DecisionContext { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>
    /// Convenience: decision-engine PlayerKnowledge when available.
    /// Prediction engines must not invent this when null.
    /// </summary>
    public PlayerKnowledge? PlayerKnowledge => Knowledge.DecisionPlayerKnowledge;
}
