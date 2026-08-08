using Playbook.Core.Intelligence.Models;
using Playbook.Core.Predictions;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;

namespace Playbook.Application.Predictions.Interfaces;

/// <summary>
/// Compares market lines to Playbook football projections (not fantasy scoring).
/// </summary>
public interface IQuickPicksEngine
{
    string Version { get; }

    Prediction? Evaluate(
        PropLine line,
        decimal? playbookProjection,
        int projectionConfidence,
        int volatility,
        PlayerIntelligenceProfile? intelligence,
        PlayerStatisticalContext? statisticalContext,
        string? injuryNote);
}
