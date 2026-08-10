using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;

namespace Playbook.Core.Decisions;

/// <summary>
/// Context required to evaluate a decision. Designed to support live weeks and future historical replay
/// via <see cref="InformationCutoff"/> and season/week stamps.
/// </summary>
public sealed class DecisionContext
{
    public required Guid? LeagueId { get; init; }

    public required int? SelectedRosterId { get; init; }

    public required string LeagueName { get; init; }

    public required ScoringType ScoringType { get; init; }

    public required int Season { get; init; }

    public required int Week { get; init; }

    /// <summary>
    /// Only information at or before this timestamp should influence the decision when set.
    /// Null means "current available knowledge" (live mode).
    /// </summary>
    public DateTimeOffset? InformationCutoff { get; init; }

    public string? TeamName { get; init; }

    public DecisionKind DecisionKind { get; init; } = DecisionKind.StartSit;

    public static DecisionContext FromLeague(League? league, FantasyTeam? team, DecisionKind kind = DecisionKind.StartSit) =>
        new()
        {
            LeagueId = league?.Id,
            SelectedRosterId = league?.SelectedRosterId ?? team?.RosterId,
            LeagueName = league?.Name ?? "No League Selected",
            ScoringType = league?.ScoringType ?? ScoringType.Ppr,
            Season = league?.Season ?? DateTime.UtcNow.Year,
            Week = league?.CurrentWeek ?? 1,
            InformationCutoff = null,
            TeamName = team is null
                ? null
                : string.IsNullOrWhiteSpace(team.TeamName) ? team.DisplayName : team.TeamName,
            DecisionKind = kind
        };
}

public enum DecisionKind
{
    StartSit = 0,
    Watch = 1,
    Waiver = 2,
    Trade = 3,
    Matchup = 4,
    Generic = 5
}

public enum DecisionRecommendation
{
    Start = 0,
    Sit = 1,
    Watch = 2,
    NoAction = 3
}

/// <summary>
/// Distinguishes raw expectation from actionable decision quality.
/// Higher projected points alone must not always win.
/// </summary>
public sealed class ValueAssessment
{
    /// <summary>Projection-driven expectation (points-oriented).</summary>
    public required double ExpectedValue { get; init; }

    /// <summary>
    /// Decision-oriented score after evidence quality, health, role certainty, and alternatives.
    /// This is what ranking/recommendation should primarily use.
    /// </summary>
    public required double DecisionValue { get; init; }

    public required string MethodologyNote { get; init; }
}

/// <summary>
/// Inference derived from facts/signals — not itself a fact or a final decision.
/// Example: "Current injury designation weakens start viability."
/// </summary>
public sealed class DecisionInference
{
    public required string Statement { get; init; }

    public required SignalDirection Direction { get; init; }

    public required int Confidence { get; init; }

    public required IReadOnlyList<string> BasedOnFactKeys { get; init; }
}

/// <summary>
/// Traceable decision output from the centralized engine.
/// </summary>
public sealed class DecisionResult
{
    public required Guid DecisionId { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required string PositionLabel { get; init; }

    public required DecisionRecommendation Recommendation { get; init; }

    /// <summary>
    /// v1 evidence-quality confidence (0–100). Not statistically calibrated.
    /// See DecisionEngine confidence methodology comments.
    /// </summary>
    public required int Confidence { get; init; }

    public required ValueAssessment Values { get; init; }

    public required IReadOnlyList<KnowledgeFact> Facts { get; init; }

    public required IReadOnlyList<DecisionInference> Inferences { get; init; }

    public required IReadOnlyList<KnowledgeSignal> SupportingEvidence { get; init; }

    public required IReadOnlyList<KnowledgeSignal> OpposingEvidence { get; init; }

    public required IReadOnlyList<string> Unknowns { get; init; }

    public required IReadOnlyList<string> Conflicts { get; init; }

    public required IReadOnlyList<string> Rationale { get; init; }

    public required IReadOnlyList<Guid> AlternativesConsidered { get; init; }

    public required bool IsProvisional { get; init; }

    public required EvidenceStatus EvidenceStatus { get; init; }

    public string? ProjectionSummary { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>
/// Persistable decision record for live use and future historical replay/evaluation.
/// ActualOutcome / EvaluationResult remain null until graded.
/// </summary>
public sealed class DecisionRecord
{
    public required Guid DecisionId { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required DecisionKind DecisionType { get; init; }

    public required DecisionRecommendation Recommendation { get; init; }

    public required int Confidence { get; init; }

    public required Guid? LeagueId { get; init; }

    public required int? SelectedRosterId { get; init; }

    public required string LeagueName { get; init; }

    public required ScoringType ScoringType { get; init; }

    public required int Season { get; init; }

    public required int Week { get; init; }

    public DateTimeOffset? InformationCutoff { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required IReadOnlyList<string> SupportingEvidence { get; init; }

    public required IReadOnlyList<string> OpposingEvidence { get; init; }

    public required IReadOnlyList<string> Unknowns { get; init; }

    public required IReadOnlyList<Guid> AlternativesConsidered { get; init; }

    public required double ExpectedValue { get; init; }

    public required double DecisionValue { get; init; }

    public required IReadOnlyList<string> Rationale { get; init; }

    /// <summary>Filled by future historical replay.</summary>
    public double? ActualOutcome { get; init; }

    /// <summary>Filled by future evaluation/calibration.</summary>
    public string? EvaluationResult { get; init; }
}

/// <summary>Roster candidate for comparative start/sit evaluation.</summary>
public sealed class StartSitCandidate
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Position Position { get; init; }

    public required bool IsStarter { get; init; }
}

/// <summary>
/// Batch output from centralized Start/Sit synthesis.
/// UI consumes <see cref="Recommendations"/>; replay/debug uses <see cref="Decisions"/>.
/// </summary>
public sealed class StartSitDecisionBatch
{
    public required IReadOnlyList<StartSitRecommendation> Recommendations { get; init; }

    public required IReadOnlyList<DecisionResult> Decisions { get; init; }
}
