namespace Playbook.Application.Research;

/// <summary>
/// Runs the postgame reconciliation pass: finds snapshots whose event has concluded, retrieves
/// actual production, grades them, and persists the result. Never touches Projection V2,
/// Confidence V2, the Confidence-Aware Decision Policy, or any live prediction output — purely
/// additive research memory.
/// </summary>
public interface IPostEventReconciliationService
{
    /// <summary>Returns the number of snapshots graded in this pass.</summary>
    int RunPendingReconciliation();
}
