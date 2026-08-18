using Playbook.Core.Historical;

namespace Playbook.Core.Draft;

/// <summary>Whether an actual pick matched Playbook's top recommendation at the time it was made.</summary>
public enum PersonalDecisionAlignment { Unknown, MatchedRecommendation, WentOffBoard }

public sealed record PersonalPositionEmphasis(string Position, int PickCount, double AverageRound);

/// <summary>
/// One observed pick compared against the recommendation captured on the most recent poll where
/// the user was on the clock for it. <see cref="MatchedCategory"/> is set only when the pick
/// matches one of that poll's diverse recommendation categories (Best Overall / Best Value / Best
/// Upside / Safest Floor / Safe To Wait On) — this is how floor-vs-upside and reach-vs-wait lean
/// surface, without inventing a separate scoring pass.
/// </summary>
public sealed record PersonalDraftDecision(
    int PickNumber,
    Guid PickedPlayerId,
    string PickedPlayerName,
    RecommendationCategory? MatchedCategory,
    PersonalDecisionAlignment Alignment);

/// <summary>
/// Temporary, session-only observations about how the user is drafting in the currently
/// attached/ingested mock. Never written to persistent storage and never blended into the
/// historical model — one mock's tendencies must not permanently alter league-wide intelligence.
/// <see cref="EvidenceStrength"/> gates every field: a handful of picks stays
/// Insufficient/Limited rather than overclaiming a pattern.
/// </summary>
public sealed record PersonalDraftTendencies(
    int PickCount,
    IReadOnlyList<PersonalPositionEmphasis> PositionEmphasis,
    string RosterBuildPattern,
    IReadOnlyDictionary<RecommendationCategory, int> CategoryPickCounts,
    IReadOnlyList<PersonalDraftDecision> DecisionsVsRecommendations,
    HistoricalEvidenceStrength EvidenceStrength);
