namespace Playbook.Core.Draft;

/// <summary>Top-level output for the Draft Assistant UI — always reflects real Sleeper state.
/// Never fabricates a board, a pick, or a recommendation when data is unavailable.</summary>
public sealed class DraftAssistantReport
{
    public DraftBoard? Board { get; init; }

    public required bool IsOnTheClock { get; init; }

    public DraftRecommendation? Recommended { get; init; }

    public required IReadOnlyList<DraftRecommendation> Alternatives { get; init; }

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
}
