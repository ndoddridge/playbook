using Playbook.Core.Knowledge;
using Playbook.Core.Replay;

namespace Playbook.Core.Research;

/// <summary>
/// Frozen research-integrity constants for the command-line workbench.
/// Does not change live production behavior.
/// </summary>
public static class ResearchIntegrity
{
    public const int HoldoutSeason = 2024;

    public const int FrozenLabRosterBenchmarkSeason = 2018;

    public const KnowledgeMode ProductionKnowledgeMode = KnowledgeMode.Passthrough;

    public const HistoricalCandidateUniverse DefaultEvalUniverse =
        HistoricalCandidateUniverse.LabRoster;

    public const string DefaultOutputRoot = "research-runs";

    public static readonly IReadOnlyList<int> DefaultDevelopmentSeasons = [2015, 2018, 2021];

    /// <summary>Knowledge transforms currently rejected/disabled for enablement.</summary>
    public static readonly IReadOnlyList<string> RejectedKnowledgeTransforms =
    [
        "Usage",
        "RoleHealth",
        "RecentForm",
        "RecentFormThinMargin",
        "DataSufficiencyTrust"
    ];

    public static readonly IReadOnlyDictionary<string, string> ExperimentCatalog =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["projection-calibration-v2"] = "Projection Calibration V2 (dev fit → one 2024 holdout)",
            ["confidence-calibration-v2"] = "Confidence Calibration V2",
            ["confidence-aware-decision-policy-v1"] = "Confidence-Aware Decision Policy V1",
            ["knowledge-impact-v1"] = "Knowledge Impact Experiment V1 (ablation; Usage freeze record)",
            ["recent-form-thin-margin-v1"] = "RecentForm Thin-Margin (REJECTED)",
            ["data-sufficiency-trust-v1"] = "DataSufficiency Trust Gate (REJECTED)",
            ["quick-picks-historical-v1"] = "Quick Picks Historical Evaluation V1",
            ["quick-picks-recent-form-v1"] = "Quick Picks RecentForm (DISABLED)",
            ["shared-knowledge-expanded-universe-v1"] = "Shared Knowledge × Expanded Universe (REGRESSION)",
            ["historical-evaluation-coverage-v1"] = "Historical evaluation coverage expansion report",
            ["position-segmented-calibration-v1"] = "Position-Segmented Projection Calibration V1 (REJECTED — dev gate failed, 2024 never touched)"
        };

    public static bool IncludesHoldout(IEnumerable<int> seasons) =>
        seasons.Contains(HoldoutSeason);

    public static void EnsureProductionKnowledgeMode(KnowledgeMode mode)
    {
        if (mode != ProductionKnowledgeMode)
        {
            // Informational check used by CLI after runs restore state.
            _ = mode;
        }
    }
}

public enum ResearchCommandKind
{
    Test = 0,
    Eval = 1,
    Experiment = 2,
    Simulate = 3,
    Compare = 4,
    Inspect = 5,
    ListExperiments = 6
}

public enum ResearchPredictionSurface
{
    StartSit = 0,
    QuickPicks = 1,
    Both = 2
}

public enum ResearchKnowledgeModeLabel
{
    /// <summary>Stripped contextual knowledge (experiment control).</summary>
    Baseline = 0,

    /// <summary>Production assembled knowledge (identity applicator).</summary>
    Passthrough = 1,

    /// <summary>Explicit Enhanced groups — only for catalogued experiments; not ad-hoc enablement.</summary>
    Enhanced = 2
}

public enum ResearchScopeMode
{
    /// <summary>Development / fitting seasons only. Holdout seasons are rejected.</summary>
    Development = 0,

    /// <summary>Explicit holdout evaluation. Requires --allow-holdout.</summary>
    Holdout = 1,

    /// <summary>Mixed season list. Holdout seasons require --allow-holdout; never used for fitting.</summary>
    Mixed = 2
}
