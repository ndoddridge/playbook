using Playbook.Application.Projections;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Research.Experiments;

/// <summary>
/// Experiment: Era-Segmented Position Baselines (Hypothesis #1)
/// 
/// Objective: Test whether position-specific baselines segmented by era (2012-2019 vs 2020-2023)
/// improve projection quality and downstream Start/Sit decision accuracy.
/// 
/// Design:
/// - Control: Frozen hardcoded baselines (era-agnostic)
/// - Experimental: Era-segmented baselines reflecting NFL passing-game evolution
/// - LOOCV: Dev seasons 2015, 2018, 2021 (never 2024)
/// - Anti-leakage: Baselines frozen before any evaluation
/// 
/// Success Criteria:
/// - MAE reduction ≥1.5% on WR + TE + RB (affected positions)
/// - No regression on QB/K/DST (less affected positions)
/// - Per-fold consistency
/// - Deterministic results
/// </summary>
public sealed class EraSegmentedBaselinesExperimentV1
{
    public const string ExperimentId = "era-segmented-baselines-v1";
    public const string Description = "Position-specific baselines segmented by era (2012-2019 vs 2020-2023)";
    public const string HypothesisTitle = "Era-Segmented Position Baselines Improve Projection Quality";
    
    /// <summary>
    /// Era A: 2012-2019 (pre-passing-game evolution)
    /// Era B: 2020-2023 (post-passing-game evolution with high-vol slot WRs, TE receiving inflation)
    /// 
    /// Rationale:
    /// - QB: Stable (18.0 → 18.5, slight inflation)
    /// - RB: Declining (12.0 → 11.5, fewer dual-threat opportunities)
    /// - WR: Rising (10.8 → 11.5, increased target share)
    /// - TE: Rising (7.5 → 8.5, elevated in fantasy-relevant formats)
    /// - K/DST: Stable (8.0 → 8.0, position-agnostic to passing game)
    /// </summary>
    public static class BaselineConfiguration
    {
        public static Dictionary<string, decimal> EraA => new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.QB)] = 18.0m,
            [nameof(Position.RB)] = 12.0m,
            [nameof(Position.WR)] = 10.8m,
            [nameof(Position.TE)] = 7.5m,
            [nameof(Position.K)] = 8.0m,
            [nameof(Position.DST)] = 8.0m
        };

        public static Dictionary<string, decimal> EraB => new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.QB)] = 18.5m,
            [nameof(Position.RB)] = 11.5m,
            [nameof(Position.WR)] = 11.5m,
            [nameof(Position.TE)] = 8.5m,
            [nameof(Position.K)] = 8.0m,
            [nameof(Position.DST)] = 8.0m
        };

        public static Dictionary<string, decimal> Control => new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.QB)] = 18.0m,
            [nameof(Position.RB)] = 12.0m,
            [nameof(Position.WR)] = 11.0m,
            [nameof(Position.TE)] = 8.0m,
            [nameof(Position.K)] = 8.0m,
            [nameof(Position.DST)] = 8.0m
        };
    }

    /// <summary>
    /// Dev LOOCV folds (never 2024 for fitting/selection).
    /// </summary>
    public static readonly int[] DevSeasons = { 2015, 2018, 2021 };

    /// <summary>
    /// Positions expected to benefit most from era adjustment.
    /// </summary>
    public static readonly Position[] AffectedPositions = { Position.WR, Position.TE, Position.RB };

    /// <summary>
    /// Positions less affected by era shift (control group).
    /// </summary>
    public static readonly Position[] UnaffectedPositions = { Position.QB, Position.K, Position.DST };

    /// <summary>
    /// Success criteria (predefined, immutable).
    /// </summary>
    public sealed class SuccessCriteria
    {
        /// <summary>
        /// Primary: MAE reduction on affected positions (WR, TE, RB).
        /// Target: ≥1.5% absolute reduction.
        /// </summary>
        public decimal AffectedPositionsMAEReductionThreshold => 0.015m;

        /// <summary>
        /// Secondary: No regression on unaffected positions (QB, K, DST).
        /// Target: 0% or slight improvement (≤0.5% regression tolerated).
        /// </summary>
        public decimal UnaffectedPositionsMAERegressionTolerance => 0.005m;

        /// <summary>
        /// Tertiary: Per-fold consistency.
        /// Target: All folds show improvement or neutral (no regression in any single fold).
        /// </summary>
        public bool RequireAllFoldsImprove => false; // At least mean improvement acceptable

        /// <summary>
        /// Determinism: Same baseline provider should return same results across runs.
        /// </summary>
        public bool RequireDeterminism => true;
    }

    /// <summary>
    /// Anti-leakage safeguards (frozen, immutable).
    /// </summary>
    public sealed class AntiLeakageSafeguards
    {
        /// <summary>
        /// Baselines must be computed ONLY from training seasons, not validation.
        /// </summary>
        public static bool ValidateTrainingDataIsolation(int validationSeason, IReadOnlyList<int> trainingSeason) =>
            !trainingSeason.Contains(validationSeason);

        /// <summary>
        /// 2024 must never contribute to baseline fitting.
        /// </summary>
        public static bool Validate2024NotInTrainingData(IReadOnlyList<int> trainingSeason) =>
            !trainingSeason.Contains(2024);

        /// <summary>
        /// Control baseline must be frozen (era-agnostic).
        /// </summary>
        public static bool ValidateControlBaselineIsFrozen(IProjectionBaselineProvider control) =>
            control is FrozenBaselineProvider;

        /// <summary>
        /// Experimental baseline must respect era boundaries.
        /// </summary>
        public static bool ValidateExperimentalBaselineRespectsEras(IProjectionBaselineProvider experimental) =>
            experimental is EraSegmentedBaselineProvider;
    }
}

/// <summary>
/// Manifest for era-segmented baselines experiment run.
/// Tracks metadata, configuration, safeguards, and results.
/// </summary>
public sealed class EraSegmentedBaselinesExperimentManifest
{
    public string ExperimentId { get; init; } = EraSegmentedBaselinesExperimentV1.ExperimentId;
    public string Description { get; init; } = EraSegmentedBaselinesExperimentV1.Description;
    public string HypothesisTitle { get; init; } = EraSegmentedBaselinesExperimentV1.HypothesisTitle;
    
    public DateTimeOffset RunTimestamp { get; init; } = DateTimeOffset.UtcNow;
    public string RunId { get; init; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Baseline configuration used (frozen, pre-committed).
    /// </summary>
    public Dictionary<string, Dictionary<string, decimal>> BaselineConfiguration { get; init; } = new()
    {
        ["control"] = EraSegmentedBaselinesExperimentV1.BaselineConfiguration.Control,
        ["eraA"] = EraSegmentedBaselinesExperimentV1.BaselineConfiguration.EraA,
        ["eraB"] = EraSegmentedBaselinesExperimentV1.BaselineConfiguration.EraB
    };

    /// <summary>
    /// Dev LOOCV folds (never 2024).
    /// </summary>
    public int[] DevSeasons { get; init; } = EraSegmentedBaselinesExperimentV1.DevSeasons;

    /// <summary>
    /// Anti-leakage assertions.
    /// </summary>
    public bool ValidatedTrainingDataIsolation { get; init; }
    public bool Validated2024NotUsed { get; init; }
    public bool ValidatedControlIsFrozen { get; init; }
    public bool ValidatedExperimentalRespectsEras { get; init; }

    /// <summary>
    /// Per-fold results.
    /// </summary>
    public List<EraSegmentedBaselinesExperimentFoldResult> FoldResults { get; init; } = new();

    /// <summary>
    /// Aggregate results.
    /// </summary>
    public EraSegmentedBaselinesExperimentAggregateResult? AggregateResult { get; init; }

    /// <summary>
    /// Success criteria assessment.
    /// </summary>
    public SuccessCriteriaAssessment? SuccessCriteriaAssessment { get; init; }
}

/// <summary>
/// Per-fold results (one per dev season in LOOCV).
/// </summary>
public sealed class EraSegmentedBaselinesExperimentFoldResult
{
    public int ValidationSeason { get; init; }
    
    /// <summary>
    /// MAE on affected positions (WR, TE, RB).
    /// </summary>
    public decimal ControlMAEAffected { get; init; }
    public decimal ExperimentalMAEAffected { get; init; }
    public decimal MAEDifferenceAffected => ExperimentalMAEAffected - ControlMAEAffected;
    public decimal MAEPercentageChangeAffected => ControlMAEAffected > 0 
        ? MAEDifferenceAffected / ControlMAEAffected 
        : 0m;

    /// <summary>
    /// MAE on unaffected positions (QB, K, DST).
    /// </summary>
    public decimal ControlMAEUnaffected { get; init; }
    public decimal ExperimentalMAEUnaffected { get; init; }
    public decimal MAEDifferenceUnaffected => ExperimentalMAEUnaffected - ControlMAEUnaffected;
    public decimal MAEPercentageChangeUnaffected => ControlMAEUnaffected > 0 
        ? MAEDifferenceUnaffected / ControlMAEUnaffected 
        : 0m;

    /// <summary>
    /// Decision value (Start/Sit ranking accuracy).
    /// </summary>
    public decimal ControlDecisionValue { get; init; }
    public decimal ExperimentalDecisionValue { get; init; }
    public decimal DecisionValueDifference => ExperimentalDecisionValue - ControlDecisionValue;

    /// <summary>
    /// Graded decisions count.
    /// </summary>
    public int GradedDecisionsCount { get; init; }
}

/// <summary>
/// Aggregate results across all dev folds.
/// </summary>
public sealed class EraSegmentedBaselinesExperimentAggregateResult
{
    public decimal MeanMAEAffected_Control { get; init; }
    public decimal MeanMAEAffected_Experimental { get; init; }
    public decimal MeanMAEChangeAffected => MeanMAEAffected_Experimental - MeanMAEAffected_Control;
    public decimal MeanMAEPercentageChangeAffected => MeanMAEAffected_Control > 0 
        ? MeanMAEChangeAffected / MeanMAEAffected_Control 
        : 0m;

    public decimal MeanMAEUnaffected_Control { get; init; }
    public decimal MeanMAEUnaffected_Experimental { get; init; }
    public decimal MeanMAEChangeUnaffected => MeanMAEUnaffected_Experimental - MeanMAEUnaffected_Control;
    public decimal MeanMAEPercentageChangeUnaffected => MeanMAEUnaffected_Control > 0 
        ? MeanMAEChangeUnaffected / MeanMAEUnaffected_Control 
        : 0m;

    public decimal MeanDecisionValue_Control { get; init; }
    public decimal MeanDecisionValue_Experimental { get; init; }
    public decimal MeanDecisionValueChange => MeanDecisionValue_Experimental - MeanDecisionValue_Control;

    public int TotalGradedDecisions { get; init; }
    public int FoldCount { get; init; }
}

/// <summary>
/// Success criteria assessment against predefined thresholds.
/// </summary>
public sealed class SuccessCriteriaAssessment
{
    public bool PrimaryMetSucceeded { get; init; } // MAE reduction ≥1.5% on affected positions
    public string PrimaryMetSummary { get; init; } = string.Empty;

    public bool SecondaryMetSucceeded { get; init; } // No regression on unaffected positions
    public string SecondaryMetSummary { get; init; } = string.Empty;

    public bool TertiaryMetSucceeded { get; init; } // Per-fold consistency
    public string TertiaryMetSummary { get; init; } = string.Empty;

    public bool DeterminismMetSucceeded { get; init; } // Deterministic results
    public string DeterminismMetSummary { get; init; } = string.Empty;

    public bool OverallSucceeded => 
        PrimaryMetSucceeded && SecondaryMetSucceeded && TertiaryMetSucceeded && DeterminismMetSucceeded;

    public string OverallAssessment { get; init; } = string.Empty;
}
