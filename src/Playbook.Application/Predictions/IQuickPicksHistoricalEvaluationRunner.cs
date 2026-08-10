using Playbook.Core.Leagues;
using Playbook.Core.Predictions;

namespace Playbook.Application.Predictions;

/// <summary>
/// Deterministic historical Quick Picks replay + grading harness.
/// Does not change live Quick Picks UI or production scoring.
/// </summary>
public interface IQuickPicksHistoricalEvaluationRunner
{
    /// <summary>Evaluate one week under Baseline or Enhanced mode.</summary>
    Task<QuickPickSeasonScorecard> RunWeekAsync(
        int season,
        int week,
        QuickPickMode mode,
        string? fixtureId = "nflverse",
        ScoringType scoringType = ScoringType.Ppr,
        CancellationToken cancellationToken = default);

    /// <summary>Evaluate one season under Baseline or Enhanced mode.</summary>
    Task<QuickPickSeasonScorecard> RunSeasonAsync(
        int season,
        QuickPickMode mode,
        string? fixtureId = "nflverse",
        ScoringType scoringType = ScoringType.Ppr,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Official V1 protocol: development Baseline + Enhanced, freeze evaluator,
    /// then exactly one 2024 holdout. Must not use 2024 during development.
    /// </summary>
    Task<QuickPicksHistoricalEvaluationReport> RunOfficialEvaluationAsync(
        CancellationToken cancellationToken = default);
}
