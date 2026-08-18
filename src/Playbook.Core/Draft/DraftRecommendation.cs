namespace Playbook.Core.Draft;

/// <summary>
/// One ranked draft candidate. Deliberately carries two distinct ranks —
/// <see cref="BestPlayerAvailableRank"/> (pure projected value, ignoring roster context) and
/// <see cref="TeamFitRank"/> (adjusted for this team's roster construction and positional
/// scarcity at this exact draft position) — because a highly-ranked player can still be a poor
/// recommendation, and a lower-ranked player can be the correct pick.
/// </summary>
public sealed class DraftRecommendation
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required string PositionLabel { get; init; }

    public required string Team { get; init; }

    /// <summary>Real weekly projection from IProjectionService. Null when no legitimate projection exists.</summary>
    public decimal? ProjectedPoints { get; init; }

    /// <summary>ProjectedPoints minus the projection of the best remaining player at the same
    /// position once this pick is made — a real, computed replacement-value gap, not a market price.</summary>
    public decimal? ValueOverReplacement { get; init; }

    /// <summary>Rank among all remaining undrafted players by raw projected production alone.</summary>
    public required int BestPlayerAvailableRank { get; init; }

    /// <summary>Rank after this team's roster construction/positional scarcity are factored in.</summary>
    public required int TeamFitRank { get; init; }

    public required int Confidence { get; init; }

    public required string Reasoning { get; init; }

    public required IReadOnlyList<DraftRecommendationFactor> Factors { get; init; }

    /// <summary>Which strategic role this pick plays on the board — see <see cref="RecommendationCategory"/>.</summary>
    public RecommendationCategory Category { get; init; } = RecommendationCategory.None;

    /// <summary>Plain-language reason this candidate earned its category. Empty for <see cref="RecommendationCategory.None"/>.</summary>
    public string CategoryRationale { get; init; } = "";
}
