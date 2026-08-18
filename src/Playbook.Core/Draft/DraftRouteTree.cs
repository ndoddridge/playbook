namespace Playbook.Core.Draft;

/// <summary>
/// A branch of the route tree: what to recommend if <see cref="TriggerPlayerName"/> is drafted
/// before the user's next pick.
/// </summary>
public sealed record DraftRouteBranch(Guid TriggerPlayerId, string TriggerPlayerName, DraftRecommendation ThenRecommend);

/// <summary>
/// A small decision tree derived from already-computed recommendations — no new scoring or
/// simulation. Recomputed on every report, including while another team is on the clock, so it
/// keeps updating even when it isn't the user's pick.
/// </summary>
public sealed record DraftRouteTree(
    DraftRecommendation? BestCurrentMove,
    IReadOnlyList<DraftRecommendation> Alternatives,
    DraftRecommendation? LikelyNextTarget,
    IReadOnlyList<DraftRouteBranch> IfTakenBranches);
