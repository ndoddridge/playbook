using Playbook.Core.Intelligence.Models;
using Playbook.Core.Replay;

namespace Playbook.Application.Replay;

/// <summary>
/// Post-ranking policy layer that may suppress, reclassify, or swap recommendations
/// using calibrated confidence as a trust signal. Projection and confidence formulas stay frozen.
/// </summary>
public interface IConfidenceAwareDecisionPolicy
{
    ConfidenceAwareDecisionPolicyDefinition ActiveDefinition { get; }

    ConfidenceAwareDecisionPolicyApplicationResult Apply(
        IReadOnlyList<StartSitRecommendation> rankedRecommendations,
        ConfidenceAwareDecisionPolicyDefinition? overrideDefinition = null);
}

public sealed record ConfidenceAwareDecisionPolicyApplicationResult(
    IReadOnlyList<StartSitRecommendation> Recommendations,
    int AffectedCount,
    int SuppressedCount,
    int SwappedCount,
    int LowTrustLabeledCount,
    IReadOnlyList<string> Notes);
