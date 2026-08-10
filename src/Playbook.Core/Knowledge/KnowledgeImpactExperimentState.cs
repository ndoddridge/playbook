namespace Playbook.Core.Knowledge;

/// <summary>
/// Runtime switch for Knowledge Impact experiments.
/// Default Passthrough preserves frozen-era behavior (identity).
/// Experiment runners set Baseline / Enhanced explicitly.
/// </summary>
public sealed class KnowledgeImpactExperimentState
{
    public KnowledgeMode Mode { get; set; } = KnowledgeMode.Passthrough;

    /// <summary>Groups applied when Mode is Enhanced. Ignored otherwise.</summary>
    public KnowledgeImpactGroup ActiveGroups { get; set; } = KnowledgeImpactGroup.None;

    /// <summary>
    /// Thin-margin gate ceiling (projection units). Used when ActiveGroups includes RecentFormThinMargin.
    /// </summary>
    public double ThinMarginMaxPoints { get; set; } =
        FrozenRecentFormThinMarginExperimentV1.ThinMarginMaxPoints;

    public void ConfigurePassthrough()
    {
        Mode = KnowledgeMode.Passthrough;
        ActiveGroups = KnowledgeImpactGroup.None;
        ThinMarginMaxPoints = FrozenRecentFormThinMarginExperimentV1.ThinMarginMaxPoints;
    }

    public void ConfigureBaseline()
    {
        Mode = KnowledgeMode.Baseline;
        ActiveGroups = KnowledgeImpactGroup.None;
        ThinMarginMaxPoints = FrozenRecentFormThinMarginExperimentV1.ThinMarginMaxPoints;
    }

    public void ConfigureEnhanced(KnowledgeImpactGroup groups)
    {
        Mode = KnowledgeMode.Enhanced;
        ActiveGroups = groups;
        ThinMarginMaxPoints = FrozenRecentFormThinMarginExperimentV1.ThinMarginMaxPoints;
    }
}
