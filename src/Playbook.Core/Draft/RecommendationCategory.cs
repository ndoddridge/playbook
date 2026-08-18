namespace Playbook.Core.Draft;

/// <summary>
/// The strategic role a recommendation plays, so the board can expose distinct choices instead of
/// five near-identical picks. Only categories with a genuinely distinct, meaningfully competitive
/// candidate are ever surfaced — see DraftAssistantService.BuildDiverseRecommendations.
/// </summary>
public enum RecommendationCategory
{
    /// <summary>Plain team-fit fill; no distinct strategic label applies.</summary>
    None = 0,

    /// <summary>Highest combined production + scarcity + roster fit right now.</summary>
    BestOverall = 1,

    /// <summary>The single biggest real gap over the next-best player at the position.</summary>
    BestValue = 2,

    /// <summary>Highest realistic ceiling among the competitive candidates.</summary>
    BestUpside = 3,

    /// <summary>Highest realistic floor among the competitive candidates.</summary>
    SafestFloor = 4,

    /// <summary>Good player, but this position is deep enough to comfortably wait on.</summary>
    SafeToWaitOn = 5
}

public static class RecommendationCategoryPolicy
{
    public static string DisplayName(RecommendationCategory category) => category switch
    {
        RecommendationCategory.BestOverall => "Best overall",
        RecommendationCategory.BestValue => "Best value",
        RecommendationCategory.BestUpside => "Best upside",
        RecommendationCategory.SafestFloor => "Safest floor",
        RecommendationCategory.SafeToWaitOn => "Fine to wait on",
        _ => ""
    };
}
