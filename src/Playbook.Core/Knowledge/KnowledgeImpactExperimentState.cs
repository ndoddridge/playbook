namespace Playbook.Core.Knowledge;

/// <summary>
/// Runtime switch for Knowledge Impact Experiment V1.
/// Default Passthrough preserves frozen-era behavior (identity).
/// Experiment runners set Baseline / Enhanced explicitly.
/// </summary>
public sealed class KnowledgeImpactExperimentState
{
    public KnowledgeMode Mode { get; set; } = KnowledgeMode.Passthrough;

    /// <summary>Groups applied when Mode is Enhanced. Ignored otherwise.</summary>
    public KnowledgeImpactGroup ActiveGroups { get; set; } = KnowledgeImpactGroup.None;

    public void ConfigurePassthrough()
    {
        Mode = KnowledgeMode.Passthrough;
        ActiveGroups = KnowledgeImpactGroup.None;
    }

    public void ConfigureBaseline()
    {
        Mode = KnowledgeMode.Baseline;
        ActiveGroups = KnowledgeImpactGroup.None;
    }

    public void ConfigureEnhanced(KnowledgeImpactGroup groups)
    {
        Mode = KnowledgeMode.Enhanced;
        ActiveGroups = groups;
    }
}
