using Playbook.Core.Players;

namespace Playbook.Core.Replay;

/// <summary>
/// Position-Segmented Projection Calibration V1 — tests whether fitting separate piecewise
/// calibration parameters per position group beats the single frozen global Projection V2
/// curve (<see cref="FrozenProjectionCalibrationV2"/>). Development seasons only for
/// fitting/grouping/selection; 2024 is touched by a single post-freeze holdout pass, only if
/// development results justify it. Reuses <c>ProjectionCalibrationFitter</c> per group —
/// no new fitting math.
/// </summary>
public static class PositionSegmentedCalibrationExperiment
{
    public const string ExperimentId = "position-segmented-calibration-v1";

    public static readonly IReadOnlyList<int> DevelopmentSeasons = FrozenProjectionCalibrationV2.DevelopmentSeasons;

    public const int HoldoutSeason = FrozenProjectionCalibrationV2.HoldoutSeason;

    /// <summary>
    /// Minimum development observations required in EVERY individual dev season for a position
    /// to justify its own calibration group. Set comfortably above the fitter's internal
    /// per-bucket minimums (10 observations for a piecewise scale bucket, 20 for an affine OLS
    /// bucket — see <c>ProjectionCalibrationFitter</c>) so the thinner 2-season LOOCV training
    /// fold still clears those minimums. Positions that fall short are folded into an adjacent,
    /// football-defensible group (TE into WR — a standard "pass-catcher" grouping) rather than
    /// assumed up front; the grouping actually used is decided at runtime from real counts and
    /// recorded in <see cref="PositionSegmentedCalibrationExperimentReport.GroupingRationale"/>.
    /// </summary>
    public const int MinObservationsPerSeasonForOwnGroup = 25;
}

/// <summary>Pre-registered dev-scope gate — decided BEFORE the 2024 holdout is touched.</summary>
public static class PositionSegmentedCalibrationSuccessCriteria
{
    /// <summary>
    /// Primary: pooled dev LOOCV MAE of the segmented model must be at least this much lower
    /// (relative) than the pooled dev LOOCV MAE of the existing global V2 calibration.
    /// </summary>
    public const double MinRelativeMaeImprovementDev = 0.02;

    /// <summary>
    /// No single position group's own dev LOOCV MAE may be worse than the global V2 model's MAE
    /// on that same group's observations by more than this relative amount
    /// ("no catastrophic degradation in any position").
    /// </summary>
    public const double MaxRelativeMaeRegressionAnyGroup = 0.15;

    /// <summary>
    /// Dev pooled Start/Sit total decision value must not regress by more than this many points
    /// vs the existing global V2 primary.
    /// </summary>
    public const double MaxDecisionValueDegradationDev = 25.0;

    public const string Text =
        "SUCCESS (development scope; gates whether 2024 is touched at all) requires: " +
        "(1) pooled dev LOOCV MAE of the position-segmented model improves by >=2% (relative) vs " +
        "the existing global Projection V2 calibration; " +
        "(2) no position group's own dev LOOCV MAE is >15% worse (relative) than global V2 on that " +
        "same group's observations (no catastrophic per-position degradation); " +
        "(3) dev pooled Start/Sit total decision value does not degrade by more than 25 points vs " +
        "global V2. If any fails, the 2024 holdout is not run.";
}

/// <summary>One LOOCV fold's fitted calibration for one position group.</summary>
public sealed class PositionGroupFoldResult
{
    public required string GroupLabel { get; init; }

    public required int ValidateSeason { get; init; }

    public required int TrainingObservationCount { get; init; }

    public required int ValidationObservationCount { get; init; }

    public required ProjectionCalibrationMethod Method { get; init; }

    public required double LowIntercept { get; init; }

    public required double LowSlope { get; init; }

    public required double HighIntercept { get; init; }

    public required double HighSlope { get; init; }

    public required double ValMaeGlobalV2 { get; init; }

    public required double ValMaeSegmented { get; init; }
}

/// <summary>Pooled dev result for one position group, plus its frozen (all-dev-refit) calibration.</summary>
public sealed class PositionGroupSummary
{
    public required string GroupLabel { get; init; }

    public required IReadOnlyList<Position> Positions { get; init; }

    public required IReadOnlyDictionary<int, int> ObservationsPerSeason { get; init; }

    public required int TotalObservations { get; init; }

    public required IReadOnlyList<PositionGroupFoldResult> Folds { get; init; }

    public required double PooledLoocvMaeGlobalV2 { get; init; }

    public required double PooledLoocvMaeSegmented { get; init; }

    public required double PooledLoocvBiasGlobalV2 { get; init; }

    public required double PooledLoocvBiasSegmented { get; init; }

    /// <summary>Frozen: refit on ALL development observations for this group only (used for the
    /// holdout pass and for decision-value replay — never touches 2024).</summary>
    public required ProjectionCalibrationMethod FrozenMethod { get; init; }

    public required double FrozenLowIntercept { get; init; }

    public required double FrozenLowSlope { get; init; }

    public required double FrozenHighIntercept { get; init; }

    public required double FrozenHighSlope { get; init; }
}

/// <summary>Side-by-side global-V2-vs-segmented metrics for one position slice (dev or holdout).</summary>
public sealed class PositionSliceMetrics
{
    public required string GroupLabel { get; init; }

    public required int N { get; init; }

    public required double? MaeGlobalV2 { get; init; }

    public required double? MaeSegmented { get; init; }

    public required double? BiasGlobalV2 { get; init; }

    public required double? BiasSegmented { get; init; }
}

/// <summary>Full Position-Segmented Calibration V1 experiment report.</summary>
public sealed class PositionSegmentedCalibrationExperimentReport
{
    public required DateTimeOffset GeneratedAt { get; init; }

    public required string Hypothesis { get; init; }

    public required string SuccessCriteriaText { get; init; }

    public required IReadOnlyList<int> DevelopmentSeasons { get; init; }

    public required int HoldoutSeason { get; init; }

    public required string GroupingRationale { get; init; }

    public required IReadOnlyList<PositionGroupSummary> Groups { get; init; }

    public required double DevPooledMaeGlobalV2 { get; init; }

    public required double DevPooledMaeSegmented { get; init; }

    public required double DevPooledBiasGlobalV2 { get; init; }

    public required double DevPooledBiasSegmented { get; init; }

    public required DecisionImpactReport DevelopmentDecisionImpact { get; init; }

    public required bool DevJustifiesHoldout { get; init; }

    public required string DevGateRationale { get; init; }

    /// <summary>Null when the 2024 holdout was not run because development results did not justify it.</summary>
    public required double? HoldoutMaeGlobalV2 { get; init; }

    public required double? HoldoutMaeSegmented { get; init; }

    public required double? HoldoutBiasGlobalV2 { get; init; }

    public required double? HoldoutBiasSegmented { get; init; }

    public required IReadOnlyList<PositionSliceMetrics>? HoldoutByPosition { get; init; }

    public required DecisionImpactReport? HoldoutDecisionImpact { get; init; }

    public required ProjectionExperimentVerdict Verdict { get; init; }

    public required string VerdictRationale { get; init; }

    public required bool UsedHoldoutDuringFitting { get; init; }

    public string ToReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("POSITION-SEGMENTED PROJECTION CALIBRATION EXPERIMENT — V1");
        sb.AppendLine($"Generated: {GeneratedAt:u}");
        sb.AppendLine($"Holdout used during fitting: {UsedHoldoutDuringFitting}");
        sb.AppendLine();
        sb.AppendLine("HYPOTHESIS");
        sb.AppendLine($"  {Hypothesis}");
        sb.AppendLine();
        sb.AppendLine("SUCCESS CRITERIA (dev gate, pre-registered)");
        sb.AppendLine($"  {SuccessCriteriaText}");
        sb.AppendLine();
        sb.AppendLine("GROUPING");
        sb.AppendLine($"  {GroupingRationale}");
        sb.AppendLine();
        sb.AppendLine("PER-GROUP DEVELOPMENT LOOCV");
        foreach (var g in Groups)
        {
            sb.AppendLine(
                $"  [{g.GroupLabel}] positions={string.Join('/', g.Positions)} " +
                $"totalObs={g.TotalObservations} perSeason=" +
                string.Join(',', g.ObservationsPerSeason.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}")));
            foreach (var f in g.Folds)
            {
                sb.AppendLine(
                    $"    fold val={f.ValidateSeason} train_n={f.TrainingObservationCount} val_n={f.ValidationObservationCount} " +
                    $"method={f.Method} low=({f.LowIntercept:0.####},{f.LowSlope:0.####}) high=({f.HighIntercept:0.####},{f.HighSlope:0.####}) " +
                    $"valMae global={f.ValMaeGlobalV2:0.00} segmented={f.ValMaeSegmented:0.00}");
            }

            sb.AppendLine(
                $"    pooled LOOCV MAE: global={g.PooledLoocvMaeGlobalV2:0.00} segmented={g.PooledLoocvMaeSegmented:0.00} " +
                $"| bias: global={g.PooledLoocvBiasGlobalV2:0.00} segmented={g.PooledLoocvBiasSegmented:0.00}");
            sb.AppendLine(
                $"    frozen (refit all dev): {g.FrozenMethod} low=({g.FrozenLowIntercept:0.####},{g.FrozenLowSlope:0.####}) " +
                $"high=({g.FrozenHighIntercept:0.####},{g.FrozenHighSlope:0.####})");
        }

        sb.AppendLine();
        sb.AppendLine("DEVELOPMENT POOLED (all groups combined)");
        sb.AppendLine($"  MAE:  global V2={DevPooledMaeGlobalV2:0.00}  segmented={DevPooledMaeSegmented:0.00}");
        sb.AppendLine($"  Bias: global V2={DevPooledBiasGlobalV2:0.00}  segmented={DevPooledBiasSegmented:0.00}");
        sb.AppendLine();
        sb.AppendLine("DEVELOPMENT DECISION IMPACT (global V2 vs segmented; Start/Sit)");
        AppendDecision(sb, DevelopmentDecisionImpact);
        sb.AppendLine();
        sb.AppendLine("DEV GATE");
        sb.AppendLine($"  DevJustifiesHoldout={DevJustifiesHoldout}");
        sb.AppendLine($"  {DevGateRationale}");

        if (!DevJustifiesHoldout)
        {
            sb.AppendLine();
            sb.AppendLine("2024 HOLDOUT: NOT RUN (development results did not justify it).");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("OFFICIAL HOLDOUT 2024");
            sb.AppendLine($"  MAE:  global V2={Fmt(HoldoutMaeGlobalV2)}  segmented={Fmt(HoldoutMaeSegmented)}");
            sb.AppendLine($"  Bias: global V2={Fmt(HoldoutBiasGlobalV2)}  segmented={Fmt(HoldoutBiasSegmented)}");
            sb.AppendLine("  Per position:");
            foreach (var p in HoldoutByPosition ?? [])
            {
                sb.AppendLine(
                    $"    [{p.GroupLabel}] n={p.N} mae global={Fmt(p.MaeGlobalV2)} segmented={Fmt(p.MaeSegmented)} " +
                    $"bias global={Fmt(p.BiasGlobalV2)} segmented={Fmt(p.BiasSegmented)}");
            }

            sb.AppendLine();
            sb.AppendLine("HOLDOUT DECISION IMPACT (global V2 vs segmented; Start/Sit)");
            if (HoldoutDecisionImpact is not null)
            {
                AppendDecision(sb, HoldoutDecisionImpact);
            }
        }

        sb.AppendLine();
        sb.AppendLine("VERDICT");
        sb.AppendLine($"  {Verdict}");
        sb.AppendLine($"  {VerdictRationale}");
        return sb.ToString();

        static string Fmt(double? v) => v is null ? "n/a" : v.Value.ToString("0.00");
        static string FmtPct(double? v) => v is null ? "n/a" : $"{v.Value:0.#}%";

        static void AppendDecision(System.Text.StringBuilder target, DecisionImpactReport d)
        {
            target.AppendLine($"  Season={d.ScopeSeason}");
            target.AppendLine($"  Decisions global/segmented: {d.TotalDecisionsV1} / {d.TotalDecisionsV2} " +
                               $"(graded {d.GradedDecisionsV1?.ToString() ?? "n/a"} / {d.GradedDecisionsV2?.ToString() ?? "n/a"})");
            target.AppendLine($"  Acc global/segmented: {FmtPct(d.AccuracyV1)} / {FmtPct(d.AccuracyV2)}");
            target.AppendLine($"  Avg val global/segmented: {Fmt(d.AverageDecisionValueV1)} / {Fmt(d.AverageDecisionValueV2)}");
            target.AppendLine($"  Tot val global/segmented: {Fmt(d.TotalDecisionValueV1)} / {Fmt(d.TotalDecisionValueV2)}");
            target.AppendLine(
                $"  Changed={d.DecisionsChanged} improved={d.ChangedImproved} " +
                $"worsened={d.ChangedWorsened} unchangedOutcome={d.ChangedUnchangedOutcome}");
        }
    }
}
