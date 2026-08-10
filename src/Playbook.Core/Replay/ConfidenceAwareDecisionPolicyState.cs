namespace Playbook.Core.Replay;

/// <summary>
/// Runtime switch for the confidence-aware decision policy layer.
/// Default Off preserves control (Projection V2 + Calibrated Confidence + existing rules).
/// Mode enum lives in <see cref="ConfidenceAwareDecisionPolicyMode"/>.
/// </summary>
public sealed class ConfidenceAwareDecisionPolicyState
{
    public ConfidenceAwareDecisionPolicyMode Mode { get; set; } = ConfidenceAwareDecisionPolicyMode.Off;
}
