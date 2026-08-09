using Playbook.Core.Decisions;
using Playbook.Core.Players;

namespace Playbook.Core.Knowledge;

/// <summary>
/// Domain-level knowledge about a player/situation at an information cutoff.
/// Not owned by Start/Sit or Quick Picks — prediction types consume this.
/// </summary>
public sealed class SharedKnowledgeBundle
{
    public required Guid? PlayerId { get; init; }

    public required string? PlayerName { get; init; }

    public required Position? Position { get; init; }

    public string? Team { get; init; }

    public string? OpponentTeam { get; init; }

    public required int Season { get; init; }

    public required int Week { get; init; }

    /// <summary>All evidence is bounded by this cutoff. Null means live/unbounded.</summary>
    public DateTimeOffset? InformationCutoff { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required IReadOnlyList<KnowledgeFact> Facts { get; init; }

    public required IReadOnlyList<KnowledgeEvidence> Evidence { get; init; }

    /// <summary>Aspects explicitly unavailable — never interpreted as positive/negative.</summary>
    public required IReadOnlyList<KnowledgeAspect> UnavailableAspects { get; init; }

    public required IReadOnlyList<string> UnavailableSources { get; init; }

    public required EvidenceStatus OverallStatus { get; init; }

    /// <summary>0–100 evidence-completeness score (not calibrated probability).</summary>
    public required int KnowledgeConfidence { get; init; }

    /// <summary>
    /// Decision-engine compatible view when built for fantasy Start/Sit consumers.
    /// Null when the bundle was assembled for a non-decision prediction path.
    /// </summary>
    public PlayerKnowledge? DecisionPlayerKnowledge { get; init; }

    public IReadOnlyList<KnowledgeEvidence> PositiveEvidence =>
        Evidence.Where(e => !e.IsUnavailableMarker && e.Direction == SignalDirection.Positive).ToList();

    public IReadOnlyList<KnowledgeEvidence> NegativeEvidence =>
        Evidence.Where(e => !e.IsUnavailableMarker && e.Direction == SignalDirection.Negative).ToList();

    public IReadOnlyList<KnowledgeEvidence> UnknownEvidence =>
        Evidence.Where(e => e.Status == EvidenceStatus.Unknown || e.IsUnavailableMarker).ToList();
}
