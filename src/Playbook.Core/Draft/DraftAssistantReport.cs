namespace Playbook.Core.Draft;

/// <summary>Top-level output for the Draft Assistant UI — always reflects real Sleeper state.
/// Never fabricates a board, a pick, or a recommendation when data is unavailable.</summary>
public sealed class DraftAssistantReport
{
    public DraftBoard? Board { get; init; }

    public required bool IsOnTheClock { get; init; }

    public DraftRecommendation? Recommended { get; init; }

    public required IReadOnlyList<DraftRecommendation> Alternatives { get; init; }

    /// <summary>
    /// Compact YOUR PICK slate (Primary / Alternative / Upside) with look-ahead. Empty when
    /// nothing can be responsibly recommended. Prefer this over flattening into Recommended/
    /// Alternatives when rendering the companion UI.
    /// </summary>
    public IReadOnlyList<DraftPickRecommendation> PickSlate { get; init; } = [];

    /// <summary>Strategy snapshot rebuilt this cycle from the live roster. Null when unavailable.</summary>
    public DraftStrategyState? StrategyState { get; init; }

    public required IReadOnlyList<PositionalNeed> RosterNeeds { get; init; }

    public required string StatusMessage { get; init; }

    /// <summary>Signals the recommendation model is built to eventually use but has no real data
    /// source for yet (bye-week collision, strength of schedule, playoff schedule, market ADP,
    /// trade value) — listed honestly rather than guessed at.</summary>
    public required IReadOnlyList<string> UnavailableSignals { get; init; }

    /// <summary>True when the last Sleeper fetch failed — the board shown (if any) is a cached
    /// last-known-good state, not a live one.</summary>
    public required bool IsStale { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>True only for dynasty leagues — redraft never shows the strategy selector.</summary>
    public bool IsDynasty { get; init; }

    /// <summary>Active dynasty posture. Meaningless when <see cref="IsDynasty"/> is false.</summary>
    public DynastyStrategy Strategy { get; init; } = DynastyStrategy.Hybrid;

    /// <summary>League the report was built for, so the UI can persist the strategy choice.</summary>
    public Guid? LeagueId { get; init; }

    /// <summary>Where the draft currently is, derived from the real round count.</summary>
    public DraftPhase Phase { get; init; } = DraftPhase.Early;

    /// <summary>
    /// One-line, plain-language summary of why the top recommendation won — including when Best
    /// Available and Team Fit disagree. Empty when there is nothing to recommend.
    /// </summary>
    public string DecisionSummary { get; init; } = "";

    /// <summary>Target timing updates on every board refresh, including while another owner is on the clock.</summary>
    public IReadOnlyList<HistoricalTargetWatch> TargetWatch { get; init; } = [];

    /// <summary>Small decision tree (best move / alternatives / likely next target / "if X is
    /// taken" branches) derived from already-computed recommendations. Updates every refresh,
    /// including while another owner is on the clock.</summary>
    public DraftRouteTree? RouteTree { get; init; }

    /// <summary>Temporary, session-only observations about how the user is drafting this
    /// attached/ingested mock. Null until the user has made at least one pick. Never persisted
    /// and never blended into league-wide historical intelligence.</summary>
    public PersonalDraftTendencies? MyTendencies { get; init; }

    /// <summary>
    /// Persisted personal draft knowledge for the currently selected league + team only.
    /// Null when no league/team is selected or nothing has been learned for that scope.
    /// </summary>
    public PersonalDraftKnowledge? PersonalKnowledge { get; init; }

    /// <summary>Always reflects the active Draft Assistant league/team selection, including when empty.</summary>
    public PersonalDraftKnowledgeStatus? PersonalKnowledgeStatus { get; init; }
}
