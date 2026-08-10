using Playbook.Core.Replay;

namespace Playbook.Core.Knowledge;

/// <summary>
/// Frozen Data Sufficiency / Limited-History Trust Gate Experiment V1.
///
/// Formulation (simplest justified by architecture):
/// affect only KnowledgeConfidence / Prediction.Confidence trust —
/// do NOT re-enable RecentForm, Usage, or RoleHealth Opportunity transforms.
///
/// Sufficiency rule (cutoff-safe, already computed by HistoricalFeatureReconstructor):
///   Sufficient: prior REG games >= 3
///   Limited: 1–2 prior REG games
///   Insufficient: 0 prior REG games
///
/// Candidate Limited penalties are selected on development seasons only, then frozen
/// before the single 2024 holdout. Insufficient penalty = Limited + InsufficientExtra.
/// </summary>
public static class FrozenDataSufficiencyTrustExperimentV1
{
    public const string ExperimentId = "data-sufficiency-trust-experiment-v1";

    public static readonly IReadOnlyList<int> DevelopmentSeasons = [2015, 2018, 2021];

    public const int HoldoutSeason = 2024;

    public const KnowledgeImpactGroup ExperimentalGroups = KnowledgeImpactGroup.DataSufficiencyTrust;

    /// <summary>Development-only candidate Limited→confidence penalties (points on 0–100).</summary>
    public static readonly IReadOnlyList<int> CandidateLimitedPenalties = [8, 12, 16];

    /// <summary>Insufficient penalty = SelectedLimitedPenalty + this extra.</summary>
    public const int InsufficientExtraPenalty = 8;

    /// <summary>
    /// Selected on development LOOCV (highest mean Δ total decision value), then frozen.
    /// Default 12 until selection runs; holdout must not change this after freeze.
    /// </summary>
    public static int SelectedLimitedPenalty { get; set; } = 12;

    public static int SelectedInsufficientPenalty => SelectedLimitedPenalty + InsufficientExtraPenalty;

    public const string Hypothesis =
        "When a player has Limited or Insufficient prior-week history at the cutoff, " +
        "shared knowledge should carry less trust (lower KnowledgeConfidence). " +
        "Hypothesis: applying a player-local confidence penalty — without re-enabling " +
        "rejected Opportunity transforms — improves Start/Sit decision value on unseen 2024 data.";

    public const string MappingSummary =
        "Shared layer KnowledgeImpactGroup.DataSufficiencyTrust. " +
        "Reads cutoff-safe DataSufficiency from projection.data_sufficiency fact " +
        "(Sufficient: >=3 prior REG games; Limited: 1–2; Insufficient: 0). " +
        "Enhanced starts from Baseline (no Usage/RecentForm/RoleHealth restore) and applies: " +
        "Limited → KnowledgeConfidence − SelectedLimitedPenalty; " +
        "Insufficient → KnowledgeConfidence − (SelectedLimitedPenalty+8); " +
        "Sufficient → unchanged. Clamp [12,95]. " +
        "Quick Picks: same gate lowers Prediction.Confidence only (RankingScore unchanged).";

    public static int PenaltyFor(DataSufficiency? sufficiency)
    {
        if (sufficiency is null)
        {
            return 0;
        }

        return sufficiency switch
        {
            DataSufficiency.Limited => SelectedLimitedPenalty,
            DataSufficiency.Insufficient => SelectedInsufficientPenalty,
            _ => 0
        };
    }
}
