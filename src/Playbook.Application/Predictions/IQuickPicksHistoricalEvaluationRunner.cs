using Playbook.Core.Knowledge;
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
        KnowledgeImpactGroup? enhancedGroups = null,
        CancellationToken cancellationToken = default);

    /// <summary>Evaluate one season under Baseline or Enhanced mode.</summary>
    Task<QuickPickSeasonScorecard> RunSeasonAsync(
        int season,
        QuickPickMode mode,
        string? fixtureId = "nflverse",
        ScoringType scoringType = ScoringType.Ppr,
        KnowledgeImpactGroup? enhancedGroups = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Official V1 protocol: development Baseline + Enhanced, freeze evaluator,
    /// then exactly one 2024 holdout. Must not use 2024 during development.
    /// </summary>
    Task<QuickPicksHistoricalEvaluationReport> RunOfficialEvaluationAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Quick Picks RecentForm Experiment V1: Baseline vs Enhanced(RecentForm only).
    /// Development first (deterministic), freeze, then exactly one 2024 holdout.
    /// Does not change production KnowledgeMode default.
    /// </summary>
    Task<QuickPicksHistoricalEvaluationReport> RunOfficialRecentFormExperimentAsync(
        CancellationToken cancellationToken = default);
}
