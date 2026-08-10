namespace Playbook.Core.Knowledge;

/// <summary>
/// Frozen RecentForm Thin-Margin Experiment V1.
/// One variable: apply existing RecentForm Opportunity deltas only when ComparativeMargin is thin.
/// Does not retune RecentForm thresholds/deltas. Does not re-enable Usage/RoleHealth.
/// Production default remains KnowledgeMode.Passthrough.
/// </summary>
public static class FrozenRecentFormThinMarginExperimentV1
{
    public const string ExperimentId = "recent-form-thin-margin-experiment-v1";

    public static readonly IReadOnlyList<int> DevelopmentSeasons = [2015, 2018, 2021];

    public const int HoldoutSeason = 2024;

    public const KnowledgeImpactGroup ExperimentalGroups = KnowledgeImpactGroup.RecentFormThinMargin;

    /// <summary>
    /// Pre-registered thin-margin gate from existing weak-margin bucket (RecommendationMargin &lt; 3).
    /// Not fit on 2024. Not optimized for 2018.
    /// </summary>
    public const double ThinMarginMaxPoints = 3.0;

    public const int HighThreshold = FrozenKnowledgeImpactExperimentV1.RecentFormHighThreshold;

    public const int LowThreshold = FrozenKnowledgeImpactExperimentV1.RecentFormLowThreshold;

    public const int StartSitOpportunityDelta = FrozenKnowledgeImpactExperimentV1.RecentFormOpportunityDelta;

    public const double QuickPickOpportunityDelta = 0.6;

    public const string Hypothesis =
        "Ungated RecentForm was NEUTRAL on Start/Sit and Quick Picks (score shifts without useful reordering). " +
        "Hypothesis: gating the same RecentForm deltas to thin comparative margins (<3) concentrates " +
        "form influence where peer projections are close, improving Start/Sit decision value on unseen 2024 data.";

    public const string MappingSummary =
        "Shared layer: KnowledgeImpactGroup.RecentFormThinMargin. " +
        "ComparativeMargin = nearest same-position (Start/Sit) or same-market (Quick Picks) projection gap. " +
        "When Mode=Enhanced and margin < ThinMarginMaxPoints(3.0): apply existing RecentForm " +
        "Opportunity deltas (Start/Sit ±6; Quick Picks ±0.6) at thresholds 65/35. " +
        "Otherwise identity for the Opportunity delta. Rejected Usage/RoleHealth remain off.";
}
