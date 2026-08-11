using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Predictions;
using Playbook.Application.Replay;
using Playbook.Core.Knowledge;
using Playbook.Core.Leagues;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Knowledge;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Developer-facing helpers for executing historical replays without mutating live league UI state.
/// <code>
/// await HistoricalReplayCommands.RunAsync(services, season: 2018, week: 7);
/// await HistoricalReplayCommands.RunSeasonAsync(services, season: 2018, startWeek: 1, endWeek: 17);
/// await HistoricalReplayCommands.RunDefaultMultiSeasonBenchmarkAsync(services);
/// // or
/// await HistoricalReplayCommands.RunControlled2018Week7Async(services);
/// </code>
/// </summary>
public static class HistoricalReplayCommands
{
    public static Task<HistoricalReplayReport> RunControlled2018Week7Async(
        IServiceProvider services,
        ScoringType scoringType = ScoringType.Ppr,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            services,
            new HistoricalReplayRequest
            {
                Season = ControlledHistoricalFixture.Season,
                Week = ControlledHistoricalFixture.Week,
                ScoringType = scoringType,
                FixtureId = ControlledHistoricalFixture.FixtureId
            },
            cancellationToken);

    /// <summary>Real nflverse-backed 2018 Week 7 replay (not the synthetic leakage fixture).</summary>
    public static Task<HistoricalReplayReport> RunReal2018Week7Async(
        IServiceProvider services,
        ScoringType scoringType = ScoringType.Ppr,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            services,
            new HistoricalReplayRequest
            {
                Season = 2018,
                Week = 7,
                ScoringType = scoringType,
                FixtureId = "nflverse"
            },
            cancellationToken);

    public static Task<HistoricalReplayReport> RunAsync(
        IServiceProvider services,
        int season,
        int week,
        ScoringType scoringType = ScoringType.Ppr,
        string? fixtureId = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            services,
            new HistoricalReplayRequest
            {
                Season = season,
                Week = week,
                ScoringType = scoringType,
                FixtureId = fixtureId
            },
            cancellationToken);

    public static Task<HistoricalReplayReport> RunAsync(
        IServiceProvider services,
        HistoricalReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<IHistoricalReplayRunner>();
        return runner.RunAsync(request, cancellationToken);
    }

    /// <summary>Real nflverse-backed full regular-season measurement (default 2018 W1–17).</summary>
    public static Task<SeasonScorecard> RunReal2018SeasonAsync(
        IServiceProvider services,
        int startWeek = 1,
        int endWeek = 17,
        ScoringType scoringType = ScoringType.Ppr,
        CancellationToken cancellationToken = default) =>
        RunSeasonAsync(
            services,
            season: 2018,
            startWeek,
            endWeek,
            scoringType,
            fixtureId: "nflverse",
            cancellationToken);

    public static Task<SeasonScorecard> RunSeasonAsync(
        IServiceProvider services,
        int season,
        int startWeek,
        int endWeek,
        ScoringType scoringType = ScoringType.Ppr,
        string? fixtureId = "nflverse",
        CancellationToken cancellationToken = default) =>
        RunSeasonAsync(
            services,
            new MultiWeekReplayRequest
            {
                Season = season,
                StartWeek = startWeek,
                EndWeek = endWeek,
                ScoringType = scoringType,
                FixtureId = fixtureId,
                ContinueOnWeekFailure = true
            },
            cancellationToken);

    public static Task<SeasonScorecard> RunSeasonAsync(
        IServiceProvider services,
        MultiWeekReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
        return runner.RunAsync(request, cancellationToken);
    }

    /// <summary>
    /// Frozen-model multi-season benchmark using the default diverse-era sample.
    /// Does not alter projection/decision/confidence formulas.
    /// </summary>
    public static Task<MultiSeasonBenchmarkReport> RunDefaultMultiSeasonBenchmarkAsync(
        IServiceProvider services,
        ScoringType scoringType = ScoringType.Ppr,
        CancellationToken cancellationToken = default) =>
        RunMultiSeasonBenchmarkAsync(
            services,
            DefaultMultiSeasonBenchmarkSample.Seasons,
            scoringType,
            fixtureId: "nflverse",
            DefaultMultiSeasonBenchmarkSample.DefaultRoles,
            cancellationToken);

    public static Task<MultiSeasonBenchmarkReport> RunMultiSeasonBenchmarkAsync(
        IServiceProvider services,
        IReadOnlyList<int> seasons,
        ScoringType scoringType = ScoringType.Ppr,
        string? fixtureId = "nflverse",
        IReadOnlyDictionary<int, EvaluationSeasonRole>? seasonRoles = null,
        CancellationToken cancellationToken = default) =>
        RunMultiSeasonBenchmarkAsync(
            services,
            new MultiSeasonBenchmarkRequest
            {
                Seasons = seasons,
                ScoringType = scoringType,
                FixtureId = fixtureId,
                ContinueOnWeekFailure = true,
                SeasonRoles = seasonRoles ?? new Dictionary<int, EvaluationSeasonRole>()
            },
            cancellationToken);

    public static Task<MultiSeasonBenchmarkReport> RunMultiSeasonBenchmarkAsync(
        IServiceProvider services,
        MultiSeasonBenchmarkRequest request,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<IMultiSeasonHistoricalBenchmarkRunner>();
        return runner.RunAsync(request, cancellationToken);
    }

    /// <summary>
    /// Experiment 1: projection calibration. Fits on development seasons only, then ONE 2024 holdout.
    /// </summary>
    public static Task<ProjectionCalibrationExperimentReport> RunProjectionCalibrationExperimentAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<Calibration.ProjectionCalibrationExperimentRunner>();
        return runner.RunOfficialExperimentAsync(cancellationToken);
    }

    /// <summary>
    /// Experiment 2: confidence calibration under frozen Projection V2.
    /// Fits on development seasons only, then ONE 2024 holdout.
    /// </summary>
    public static Task<ConfidenceCalibrationExperimentReport> RunConfidenceCalibrationExperimentAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<Calibration.ConfidenceCalibrationExperimentRunner>();
        return runner.RunOfficialExperimentAsync(cancellationToken);
    }

    /// <summary>
    /// Experiment 3: confidence-aware decision policy under Projection V2 + Confidence V2.
    /// Fits on development seasons only, then ONE 2024 holdout.
    /// </summary>
    public static Task<ConfidenceAwareDecisionPolicyExperimentReport> RunConfidenceAwareDecisionPolicyExperimentAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<Calibration.ConfidenceAwareDecisionPolicyExperimentRunner>();
        return runner.RunOfficialExperimentAsync(cancellationToken);
    }

    /// <summary>
    /// Knowledge Impact Experiment V1: ablation of knowledge groups under Projection V2.
    /// Fits on development seasons only, then ONE 2024 holdout.
    /// </summary>
    public static Task<KnowledgeImpactExperimentReport> RunKnowledgeImpactExperimentAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<Knowledge.KnowledgeImpactExperimentRunner>();
        return runner.RunOfficialExperimentAsync(cancellationToken);
    }

    /// <summary>
    /// RecentForm Thin-Margin Experiment V1: Baseline vs Enhanced(RecentFormThinMargin).
    /// Development first, freeze, ONE 2024 holdout. Production default unchanged.
    /// </summary>
    public static Task<KnowledgeImpactExperimentReport> RunRecentFormThinMarginExperimentAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<Knowledge.KnowledgeImpactExperimentRunner>();
        return runner.RunOfficialRecentFormThinMarginExperimentAsync(cancellationToken);
    }

    /// <summary>
    /// Data Sufficiency Trust Gate Experiment V1: Baseline vs Enhanced(DataSufficiencyTrust).
    /// Selects Limited penalty on development, freezes, ONE 2024 holdout.
    /// </summary>
    public static Task<KnowledgeImpactExperimentReport> RunDataSufficiencyTrustExperimentAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<Knowledge.KnowledgeImpactExperimentRunner>();
        return runner.RunOfficialDataSufficiencyTrustExperimentAsync(cancellationToken);
    }

    /// <summary>
    /// Shared Knowledge × Expanded Universe V1: Baseline vs Passthrough on ExpandedSkillUniverse.
    /// No rejected Enhanced transforms. Development folds informational; ONE 2024 holdout.
    /// </summary>
    public static Task<SharedKnowledgeExpandedUniverseExperimentReport>
        RunSharedKnowledgeExpandedUniverseExperimentAsync(
            IServiceProvider services,
            CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<Knowledge.SharedKnowledgeExpandedUniverseExperimentRunner>();
        return runner.RunOfficialExperimentAsync(cancellationToken);
    }

    /// <summary>
    /// Quick Picks Historical Evaluation V1: Baseline then Enhanced (observational),
    /// development seasons first, then ONE 2024 holdout.
    /// </summary>
    public static Task<QuickPicksHistoricalEvaluationReport> RunQuickPicksHistoricalEvaluationAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<IQuickPicksHistoricalEvaluationRunner>();
        return runner.RunOfficialEvaluationAsync(cancellationToken);
    }

    /// <summary>Single-week historical Quick Picks evaluation (Baseline or Enhanced).</summary>
    public static Task<QuickPickSeasonScorecard> RunQuickPicksHistoricalWeekAsync(
        IServiceProvider services,
        int season,
        int week,
        QuickPickMode mode,
        string? fixtureId = "nflverse",
        ScoringType scoringType = ScoringType.Ppr,
        KnowledgeImpactGroup? enhancedGroups = null,
        HistoricalCandidateUniverse candidateUniverse = HistoricalCandidateUniverse.LabRoster,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<IQuickPicksHistoricalEvaluationRunner>();
        return runner.RunWeekAsync(
            season,
            week,
            mode,
            fixtureId,
            scoringType,
            enhancedGroups,
            candidateUniverse,
            cancellationToken);
    }

    /// <summary>
    /// Coverage expansion measurement: LabRoster vs ExpandedSkillUniverse counts only.
    /// Does not tune models. Preserves 2024 holdout isolation (reported separately).
    /// </summary>
    public static Task<HistoricalEvaluationCoverageReport> RunHistoricalEvaluationCoverageAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<HistoricalEvaluationCoverageRunner>();
        return runner.RunOfficialCoverageAsync(cancellationToken);
    }

    /// <summary>
    /// Quick Picks RecentForm Experiment V1: Baseline vs Enhanced(RecentForm only),
    /// development seasons first, then ONE 2024 holdout.
    /// </summary>
    public static Task<QuickPicksHistoricalEvaluationReport> RunQuickPicksRecentFormExperimentAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<IQuickPicksHistoricalEvaluationRunner>();
        return runner.RunOfficialRecentFormExperimentAsync(cancellationToken);
    }

    /// <summary>
    /// Position-Segmented Projection Calibration V1: per-position-group piecewise calibration vs
    /// the single frozen global Projection V2 curve. Development gate decides whether the 2024
    /// holdout is touched at all.
    /// </summary>
    public static Task<PositionSegmentedCalibrationExperimentReport> RunPositionSegmentedCalibrationExperimentAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<Calibration.PositionSegmentedCalibrationExperimentRunner>();
        return runner.RunOfficialExperimentAsync(cancellationToken);
    }
}
