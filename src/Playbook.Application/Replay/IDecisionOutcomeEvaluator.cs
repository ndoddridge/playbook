using Playbook.Core.Decisions;
using Playbook.Core.Replay;

namespace Playbook.Application.Replay;

/// <summary>
/// Grades recorded decisions against revealed historical outcomes.
/// </summary>
public interface IDecisionOutcomeEvaluator
{
    IReadOnlyList<ReplayDecisionGrade> EvaluateStartSit(
        IReadOnlyList<DecisionRecord> decisions,
        IReadOnlyList<DecisionResult> decisionResults,
        HistoricalWeekOutcomes outcomes,
        double meaningfulMarginPoints = 1.0);
}
