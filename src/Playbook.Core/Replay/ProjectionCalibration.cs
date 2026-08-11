using Playbook.Core.Players;

namespace Playbook.Core.Replay;

/// <summary>Which projection feeds Knowledge/Decision engines.</summary>
public enum HistoricalProjectionPrimaryMode
{
    /// <summary>Uncalibrated opportunity-aware baseline (Projection V1).</summary>
    ProjectionV1 = 0,

    /// <summary>Calibrated Projection V2 (Experiment 1).</summary>
    ProjectionV2 = 1,

    /// <summary>
    /// Position-segmented calibration (Experiment: position-segmented-calibration-v1).
    /// Only ever set by <c>PositionSegmentedCalibrationExperimentRunner</c>; restored to
    /// ProjectionV1 afterward. Never a production default.
    /// </summary>
    ProjectionV2PositionSegmented = 2
}

/// <summary>One (V1 prediction, actual) pair used only for calibration research.</summary>
public sealed class CalibrationObservation
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Position Position { get; init; }

    public required double V1Predicted { get; init; }

    public required double Actual { get; init; }

    public required double BaselineAPredicted { get; init; }
}

/// <summary>Frozen calibration form applied to Projection V1 outputs.</summary>
public enum ProjectionCalibrationMethod
{
    Identity = 0,
    GlobalScale = 1,
    GlobalAffine = 2,
    PiecewiseScaleAt20 = 3,
    PiecewiseAffineAt20 = 4
}

/// <summary>
/// Frozen Projection V2 calibration parameters.
/// Fitted ONLY on development seasons (2015/2018/2021) via leave-one-season-out selection,
/// then refit on all development data. 2024 must never influence these values.
/// </summary>
public static class FrozenProjectionCalibrationV2
{
    public const string ModelId = "projection-calibrated-v2";
    public const string ModelLabel = "Projection V2 · Calibrated opportunity-aware";

    /// <summary>Development seasons permitted for fitting. Holdout 2024 is excluded.</summary>
    public static readonly IReadOnlyList<int> DevelopmentSeasons = [2015, 2018, 2021];

    public const int HoldoutSeason = 2024;

    // Selected after LOOCV on development seasons — do not edit after holdout evaluation.
    public const ProjectionCalibrationMethod Method = ProjectionCalibrationMethod.PiecewiseScaleAt20;

    /// <summary>For predicted &lt; Threshold: y = LowIntercept + LowSlope * x</summary>
    public const double LowIntercept = 0.0;

    public const double LowSlope = 0.9240;

    /// <summary>Breakpoint used by piecewise methods.</summary>
    public const double Threshold = 20.0;

    /// <summary>For predicted &gt;= Threshold: y = HighIntercept + HighSlope * x</summary>
    public const double HighIntercept = 0.0;

    public const double HighSlope = 0.6005;

    public const string SelectionSummary =
        "Leave-one-season-out on {2015,2018,2021}; selected PiecewiseScaleAt20 " +
        "(scale 0.924 below 20; scale 0.6005 at/above 20). Refit on all development observations. " +
        "2024 was not used.";

    public static double Apply(double v1Predicted)
    {
        if (double.IsNaN(v1Predicted) || double.IsInfinity(v1Predicted))
        {
            return 0;
        }

        var calibrated = Method switch
        {
            ProjectionCalibrationMethod.Identity => v1Predicted,
            ProjectionCalibrationMethod.GlobalScale => HighSlope * v1Predicted,
            ProjectionCalibrationMethod.GlobalAffine => HighIntercept + (HighSlope * v1Predicted),
            ProjectionCalibrationMethod.PiecewiseScaleAt20 =>
                v1Predicted < Threshold ? LowSlope * v1Predicted : HighSlope * v1Predicted,
            ProjectionCalibrationMethod.PiecewiseAffineAt20 =>
                v1Predicted < Threshold
                    ? LowIntercept + (LowSlope * v1Predicted)
                    : HighIntercept + (HighSlope * v1Predicted),
            _ => v1Predicted
        };

        return Math.Round(Math.Max(0, calibrated), 1, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Pre-registered success criteria — recorded before official 2024 holdout.</summary>
public static class ProjectionCalibrationSuccessCriteria
{
    /// <summary>Relative MAE reduction required on development LOOCV vs V1.</summary>
    public const double MinRelativeMaeImprovementDev = 0.10;

    /// <summary>Absolute bias magnitude reduction required on development LOOCV.</summary>
    public const double MinBiasMagnitudeReductionDev = 0.50;

    /// <summary>Official holdout: minimum relative MAE improvement vs V1.</summary>
    public const double MinRelativeMaeImprovementHoldout = 0.05;

    /// <summary>Official holdout: minimum |bias| reduction vs V1.</summary>
    public const double MinBiasMagnitudeReductionHoldout = 0.40;

    /// <summary>
    /// Holdout decision total-value degradation tolerance (points).
    /// Larger negative delta than this counts as decision-quality failure.
    /// </summary>
    public const double MaxDecisionValueDegradationHoldout = 50.0;

    public const string Text =
        "SUCCESS requires: (1) Dev LOOCV MAE improves >=10% vs V1; " +
        "(2) Dev LOOCV |bias| reduces >=50% vs V1; " +
        "(3) Official 2024 MAE improves >=5% vs V1; " +
        "(4) Official 2024 |bias| reduces >=40% vs V1; " +
        "(5) Official 2024 total decision value does not degrade by more than 50 points vs V1. " +
        "MAE-only improvement with worse decisions is NOT automatic success.";
}

public enum ProjectionExperimentVerdict
{
    Improvement = 0,
    NoMaterialImprovement = 1,
    Regression = 2,
    Inconclusive = 3
}

/// <summary>Side-by-side projection metrics for one evaluation scope.</summary>
public sealed class ProjectionModelMetrics
{
    public required string ModelLabel { get; init; }

    public required int N { get; init; }

    public required double? Mae { get; init; }

    public required double? Rmse { get; init; }

    public required double? Bias { get; init; }
}

/// <summary>Decision impact between two projection primaries on the same season/week set.</summary>
public sealed class DecisionImpactReport
{
    public required int ScopeSeason { get; init; }

    public required int TotalDecisionsV1 { get; init; }

    public required int TotalDecisionsV2 { get; init; }

    public required double? AccuracyV1 { get; init; }

    public required double? AccuracyV2 { get; init; }

    public required double? AverageDecisionValueV1 { get; init; }

    public required double? AverageDecisionValueV2 { get; init; }

    public required double? TotalDecisionValueV1 { get; init; }

    public required double? TotalDecisionValueV2 { get; init; }

    public required int DecisionsChanged { get; init; }

    public required int ChangedImproved { get; init; }

    public required int ChangedWorsened { get; init; }

    public required int ChangedUnchangedOutcome { get; init; }

    /// <summary>Graded (WasCorrect != null) decision count. Optional — only populated by
    /// experiments that report it explicitly; existing consumers are unaffected.</summary>
    public int? GradedDecisionsV1 { get; init; }

    public int? GradedDecisionsV2 { get; init; }
}

/// <summary>Full Experiment 1 report model.</summary>
public sealed class ProjectionCalibrationExperimentReport
{
    public required DateTimeOffset GeneratedAt { get; init; }

    public required string Hypothesis { get; init; }

    public required string MethodDescription { get; init; }

    public required string SuccessCriteriaText { get; init; }

    public required IReadOnlyList<int> DevelopmentSeasons { get; init; }

    public required int HoldoutSeason { get; init; }

    public required ProjectionCalibrationMethod SelectedMethod { get; init; }

    public required IReadOnlyList<string> LooFoldSummaries { get; init; }

    public required ProjectionModelMetrics DevV1 { get; init; }

    public required ProjectionModelMetrics DevV2 { get; init; }

    public required ProjectionModelMetrics DevBaselineA { get; init; }

    public required ProjectionModelMetrics HoldoutV1 { get; init; }

    public required ProjectionModelMetrics HoldoutV2 { get; init; }

    public required ProjectionModelMetrics HoldoutBaselineA { get; init; }

    public required DecisionImpactReport HoldoutDecisionImpact { get; init; }

    public required DecisionImpactReport DevelopmentDecisionImpact { get; init; }

    public required ProjectionExperimentVerdict Verdict { get; init; }

    public required string VerdictRationale { get; init; }

    public required bool UsedHoldoutDuringFitting { get; init; }

    public string ToReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("PROJECTION CALIBRATION EXPERIMENT — V2");
        sb.AppendLine($"Generated: {GeneratedAt:u}");
        sb.AppendLine($"Holdout used during fitting: {UsedHoldoutDuringFitting}");
        sb.AppendLine();
        sb.AppendLine("HYPOTHESIS");
        sb.AppendLine($"  {Hypothesis}");
        sb.AppendLine();
        sb.AppendLine("SUCCESS CRITERIA (pre-registered)");
        sb.AppendLine($"  {SuccessCriteriaText}");
        sb.AppendLine();
        sb.AppendLine("METHOD");
        sb.AppendLine($"  {MethodDescription}");
        sb.AppendLine($"  Selected: {SelectedMethod}");
        sb.AppendLine($"  Apply: x<{FrozenProjectionCalibrationV2.Threshold} → " +
                      $"{FrozenProjectionCalibrationV2.LowIntercept}+{FrozenProjectionCalibrationV2.LowSlope}*x; " +
                      $"else → {FrozenProjectionCalibrationV2.HighIntercept}+{FrozenProjectionCalibrationV2.HighSlope}*x");
        sb.AppendLine();
        sb.AppendLine("DEVELOPMENT LOOCV / POOLED");
        foreach (var fold in LooFoldSummaries)
        {
            sb.AppendLine($"  {fold}");
        }

        sb.AppendLine($"  V1:      n={DevV1.N} MAE={Fmt(DevV1.Mae)} RMSE={Fmt(DevV1.Rmse)} bias={Fmt(DevV1.Bias)}");
        sb.AppendLine($"  V2:      n={DevV2.N} MAE={Fmt(DevV2.Mae)} RMSE={Fmt(DevV2.Rmse)} bias={Fmt(DevV2.Bias)}");
        sb.AppendLine($"  Base A:  n={DevBaselineA.N} MAE={Fmt(DevBaselineA.Mae)} RMSE={Fmt(DevBaselineA.Rmse)} bias={Fmt(DevBaselineA.Bias)}");
        sb.AppendLine();
        sb.AppendLine("OFFICIAL HOLDOUT 2024");
        sb.AppendLine($"  V1:      n={HoldoutV1.N} MAE={Fmt(HoldoutV1.Mae)} RMSE={Fmt(HoldoutV1.Rmse)} bias={Fmt(HoldoutV1.Bias)}");
        sb.AppendLine($"  V2:      n={HoldoutV2.N} MAE={Fmt(HoldoutV2.Mae)} RMSE={Fmt(HoldoutV2.Rmse)} bias={Fmt(HoldoutV2.Bias)}");
        sb.AppendLine($"  Base A:  n={HoldoutBaselineA.N} MAE={Fmt(HoldoutBaselineA.Mae)} RMSE={Fmt(HoldoutBaselineA.Rmse)} bias={Fmt(HoldoutBaselineA.Bias)}");
        sb.AppendLine();
        sb.AppendLine("DECISION IMPACT — DEVELOPMENT");
        AppendDecision(sb, DevelopmentDecisionImpact);
        sb.AppendLine();
        sb.AppendLine("DECISION IMPACT — HOLDOUT 2024");
        AppendDecision(sb, HoldoutDecisionImpact);
        sb.AppendLine();
        sb.AppendLine("VERDICT");
        sb.AppendLine($"  {Verdict}");
        sb.AppendLine($"  {VerdictRationale}");
        return sb.ToString();

        static string Fmt(double? v) => v is null ? "n/a" : v.Value.ToString("0.00");

        static void AppendDecision(System.Text.StringBuilder sb, DecisionImpactReport d)
        {
            sb.AppendLine($"  Season={d.ScopeSeason}");
            sb.AppendLine($"  Acc V1/V2: {FmtPct(d.AccuracyV1)} / {FmtPct(d.AccuracyV2)}");
            sb.AppendLine($"  Avg val V1/V2: {Fmt(d.AverageDecisionValueV1)} / {Fmt(d.AverageDecisionValueV2)}");
            sb.AppendLine($"  Tot val V1/V2: {Fmt(d.TotalDecisionValueV1)} / {Fmt(d.TotalDecisionValueV2)}");
            sb.AppendLine(
                $"  Changed={d.DecisionsChanged} improved={d.ChangedImproved} " +
                $"worsened={d.ChangedWorsened} unchangedOutcome={d.ChangedUnchangedOutcome}");
        }

        static string FmtPct(double? v) => v is null ? "n/a" : $"{v.Value:0.#}%";
    }
}
