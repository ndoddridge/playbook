using Playbook.Core.Predictions;

namespace Playbook.Application.Predictions.Interfaces;

/// <summary>
/// Intelligence-driven Quick Picks evaluation (not fantasy scoring).
/// </summary>
public interface IQuickPicksEngine
{
    string Version { get; }

    Prediction? Evaluate(QuickPickEvaluationContext context);
}
