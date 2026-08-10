using Playbook.Core.Knowledge;

namespace Playbook.Core.Predictions;

/// <summary>
/// Frozen Quick Picks RecentForm Experiment V1.
/// Single experimental variable: Baseline vs Enhanced(RecentForm only).
/// Does not re-enable Usage / RoleHealth. Does not retune thresholds against 2024.
/// Production default remains KnowledgeMode.Passthrough.
/// </summary>
public static class FrozenQuickPicksRecentFormExperimentV1
{
    public const string ExperimentId = "quick-picks-recent-form-experiment-v1";

    public static readonly IReadOnlyList<int> DevelopmentSeasons = [2015, 2018, 2021];

    public const int HoldoutSeason = 2024;

    /// <summary>Exactly one knowledge group — RecentForm.</summary>
    public const KnowledgeImpactGroup ExperimentalGroups = KnowledgeImpactGroup.RecentForm;

    /// <summary>
    /// Existing frozen RecentForm thresholds / QP opportunity delta from Knowledge Impact V1.
    /// Not retuned for this experiment.
    /// </summary>
    public const int HighThreshold = FrozenKnowledgeImpactExperimentV1.RecentFormHighThreshold; // 65

    public const int LowThreshold = FrozenKnowledgeImpactExperimentV1.RecentFormLowThreshold; // 35

    /// <summary>
    /// Existing ApplyToQuickPickPrediction RecentForm OpportunityScore delta (±0.6).
    /// Historical RankingScore mapping:
    /// RankingScore = ProjectedValue + (AdjustedOpportunity − 50).
    /// </summary>
    public const double QuickPickOpportunityDelta = 0.6;

    public const string MappingSummary =
        "SharedKnowledge RecentProduction evidence (from HistoricalKnowledgeFactory " +
        "RecentProductionScore) is consumed via PredictionContext → " +
        "IKnowledgeImpactApplicator.ApplyToQuickPickPrediction. " +
        "When Mode=Enhanced and ActiveGroups includes RecentForm: " +
        "Value>=65 → OpportunityScore +0.6; Value<=35 and >0 → OpportunityScore −0.6. " +
        "Historical RankingScore = ProjectedValue + OpportunityDelta (bridge base OpportunityScore=50). " +
        "Thresholds and ±0.6 are the existing frozen knowledge-model mapping — not retuned on 2024.";

    public const string Hypothesis =
        "RecentForm was NEUTRAL on Start/Sit (+0.0 mean Δ). " +
        "Hypothesis: the same shared RecentForm signal can improve Quick Picks counting-stat " +
        "ranking quality (lower MAE and/or higher Top-5 hit rate) on unseen 2024 data.";
}

/// <summary>Verdict for the Quick Picks RecentForm experiment.</summary>
public static class QuickPicksRecentFormVerdictRules
{
    /// <summary>Minimum holdout MAE reduction (Baseline − Enhanced) to count as improvement.</summary>
    public const double MinHoldoutMaeImprovement = 0.25;

    /// <summary>Minimum holdout MAE increase to count as regression.</summary>
    public const double MinHoldoutMaeRegression = 0.25;

    /// <summary>Minimum share of predictions that must change for a non-neutral material claim.</summary>
    public const double MinMaterialChangeRatePercent = 1.0;

    public const string Text =
        "IMPROVEMENT if holdout MAE improves by >=0.25 without Top-5 regression >1.0pp and change rate >=1%. " +
        "REGRESSION if holdout MAE worsens by >=0.25. " +
        "NEUTRAL otherwise (including identical predictions). " +
        "Development alone never determines success. Production default stays Passthrough.";
}
