namespace Playbook.Core.Replay;

/// <summary>One graded decision used only for confidence-calibration research.</summary>
public sealed class ConfidenceCalibrationObservation
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required Guid DecisionId { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required int RawConfidence { get; init; }

    public required bool WasCorrect { get; init; }

    public required double? DecisionDifferential { get; init; }
}

/// <summary>
/// Frozen confidence calibration (Experiment 2).
/// Fitted ONLY on development seasons {2015,2018,2021} with leave-one-season-out selection.
/// 2024 must never influence these values.
/// Maps raw evidence-quality confidence → empirical success-rate confidence (0–100).
/// </summary>
public static class FrozenDecisionConfidenceCalibrationV2
{
    public const string ModelId = "decision-confidence-calibrated-v2";

    public static readonly IReadOnlyList<int> DevelopmentSeasons = [2015, 2018, 2021];

    public const int HoldoutSeason = 2024;

    /// <summary>Raw confidence bin left edges (inclusive), ascending.</summary>
    public static readonly IReadOnlyList<int> BinStarts = [0, 15, 25, 35];

    /// <summary>
    /// Empirical success rates (%) aligned with <see cref="BinStarts"/>.
    /// Fitted via leave-one-season-out on development seasons only, then refit on all development.
    /// </summary>
    public static readonly IReadOnlyList<int> CalibratedRates = [57, 67, 65, 42];

    public const string Method = "EmpiricalReliabilityBins";

    public const string SelectionSummary =
        "Leave-one-season-out on {2015,2018,2021} under Projection V2; " +
        "selected raw-confidence grid [0,15,25,35] by lowest LOOCV ECE; " +
        "mapped each bin to its empirical success rate (reliability mapping); " +
        "refit on all development graded decisions. 2024 was not used.";

    public static int Apply(int rawConfidence)
    {
        var raw = Math.Clamp(rawConfidence, 0, 100);
        var idx = 0;
        for (var i = 0; i < BinStarts.Count; i++)
        {
            if (BinStarts[i] <= raw)
            {
                idx = i;
            }
        }

        return Math.Clamp(CalibratedRates[idx], 0, 100);
    }
}

/// <summary>Pre-registered success criteria — recorded before official 2024 holdout.</summary>
public static class ConfidenceCalibrationSuccessCriteria
{
    /// <summary>Relative ECE reduction required on development LOOCV.</summary>
    public const double MinRelativeEceImprovementDev = 0.15;

    /// <summary>Relative ECE reduction required on official holdout.</summary>
    public const double MinRelativeEceImprovementHoldout = 0.10;

    /// <summary>
    /// Holdout: success rate of upper calibrated half minus lower half (percentage points).
    /// </summary>
    public const double MinHoldoutOrderingGapPp = 5.0;

    public const string Text =
        "SUCCESS requires: (1) Dev LOOCV ECE improves >=15% vs raw; " +
        "(2) Official 2024 ECE improves >=10% vs raw; " +
        "(3) Official 2024 useful ordering: upper-half calibrated confidence success " +
        "exceeds lower-half by >=5pp; " +
        "(4) Recommendations/decision values unchanged vs raw (informational calibration only). " +
        "Perfect calibration is not required — useful ordering is.";
}

/// <summary>Per-bucket calibration row.</summary>
public sealed class ConfidenceCalibrationBucketRow
{
    public required string Label { get; init; }

    public required int MinInclusive { get; init; }

    public required int MaxExclusive { get; init; }

    public required int Count { get; init; }

    public required double? AverageConfidence { get; init; }

    public required double? ActualSuccessRatePercent { get; init; }

    public required double? AverageDecisionValue { get; init; }

    public required double? TotalDecisionValue { get; init; }

    public required double? AbsoluteCalibrationGap { get; init; }
}

/// <summary>Aggregate calibration quality metrics.</summary>
public sealed class ConfidenceCalibrationMetrics
{
    public required string Label { get; init; }

    public required int GradedDecisions { get; init; }

    public required double? Ece { get; init; }

    public required double? BrierScore { get; init; }

    public required double? OrderingGapPp { get; init; }

    public required bool IsMonotonicByBucket { get; init; }

    public required IReadOnlyList<ConfidenceCalibrationBucketRow> Buckets { get; init; }
}

/// <summary>Full Experiment 2 report.</summary>
public sealed class ConfidenceCalibrationExperimentReport
{
    public required DateTimeOffset GeneratedAt { get; init; }

    public required string Hypothesis { get; init; }

    public required string MethodDescription { get; init; }

    public required string SuccessCriteriaText { get; init; }

    public required IReadOnlyList<int> DevelopmentSeasons { get; init; }

    public required int HoldoutSeason { get; init; }

    public required bool UsedHoldoutDuringFitting { get; init; }

    public required bool ProjectionV2Unchanged { get; init; }

    public required bool RecommendationsUnchangedOnHoldout { get; init; }

    public required IReadOnlyList<string> LooFoldSummaries { get; init; }

    public required ConfidenceCalibrationMetrics DevelopmentRaw { get; init; }

    public required ConfidenceCalibrationMetrics DevelopmentCalibrated { get; init; }

    public required ConfidenceCalibrationMetrics HoldoutRaw { get; init; }

    public required ConfidenceCalibrationMetrics HoldoutCalibrated { get; init; }

    public required double? HoldoutDecisionAccuracy { get; init; }

    public required double? HoldoutAverageDecisionValue { get; init; }

    public required double? HoldoutTotalDecisionValue { get; init; }

    public required double? HoldoutWorstDecisionCost { get; init; }

    public required double? HoldoutBestDecisionValue { get; init; }

    public required int RecommendationsAffectedByConfidenceThresholds { get; init; }

    public required ProjectionExperimentVerdict Verdict { get; init; }

    public required string VerdictRationale { get; init; }

    public string ToReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("CONFIDENCE CALIBRATION EXPERIMENT — V2");
        sb.AppendLine($"Generated: {GeneratedAt:u}");
        sb.AppendLine($"Holdout used during fitting: {UsedHoldoutDuringFitting}");
        sb.AppendLine($"Projection V2 unchanged: {ProjectionV2Unchanged}");
        sb.AppendLine($"Recommendations unchanged on holdout: {RecommendationsUnchangedOnHoldout}");
        sb.AppendLine();
        sb.AppendLine("HYPOTHESIS");
        sb.AppendLine($"  {Hypothesis}");
        sb.AppendLine();
        sb.AppendLine("SUCCESS CRITERIA (pre-registered)");
        sb.AppendLine($"  {SuccessCriteriaText}");
        sb.AppendLine();
        sb.AppendLine("METHOD");
        sb.AppendLine($"  {MethodDescription}");
        sb.AppendLine(
            $"  Mapping bins: [{string.Join(',', FrozenDecisionConfidenceCalibrationV2.BinStarts)}] → " +
            $"[{string.Join(',', FrozenDecisionConfidenceCalibrationV2.CalibratedRates)}]");
        sb.AppendLine();
        sb.AppendLine("DEVELOPMENT LOOCV");
        foreach (var fold in LooFoldSummaries)
        {
            sb.AppendLine($"  {fold}");
        }

        AppendMetrics(sb, "DEV RAW", DevelopmentRaw);
        AppendMetrics(sb, "DEV CALIBRATED", DevelopmentCalibrated);
        sb.AppendLine();
        sb.AppendLine("OFFICIAL HOLDOUT 2024");
        AppendMetrics(sb, "HOLDOUT RAW", HoldoutRaw);
        AppendMetrics(sb, "HOLDOUT CALIBRATED", HoldoutCalibrated);
        sb.AppendLine(
            $"  Decision accuracy/value (unchanged by confidence remap): " +
            $"acc={FmtPct(HoldoutDecisionAccuracy)} avgVal={Fmt(HoldoutAverageDecisionValue)} " +
            $"tot={Fmt(HoldoutTotalDecisionValue)} worst={Fmt(HoldoutWorstDecisionCost)} " +
            $"best={Fmt(HoldoutBestDecisionValue)}");
        sb.AppendLine(
            $"  Recommendations affected by confidence thresholds: {RecommendationsAffectedByConfidenceThresholds}");
        sb.AppendLine();
        sb.AppendLine("VERDICT");
        sb.AppendLine($"  {Verdict}");
        sb.AppendLine($"  {VerdictRationale}");
        return sb.ToString();

        static void AppendMetrics(System.Text.StringBuilder sb, string title, ConfidenceCalibrationMetrics m)
        {
            sb.AppendLine(
                $"  {title}: n={m.GradedDecisions} ECE={Fmt(m.Ece)} Brier={Fmt(m.BrierScore)} " +
                $"orderGap={Fmt(m.OrderingGapPp)}pp monotonic={m.IsMonotonicByBucket}");
            foreach (var b in m.Buckets)
            {
                sb.AppendLine(
                    $"    {b.Label}: n={b.Count} avgConf={Fmt(b.AverageConfidence)} " +
                    $"success={FmtPct(b.ActualSuccessRatePercent)} avgVal={Fmt(b.AverageDecisionValue)} " +
                    $"gap={Fmt(b.AbsoluteCalibrationGap)}");
            }
        }

        static string Fmt(double? v) => v is null ? "n/a" : v.Value.ToString("0.00");
        static string FmtPct(double? v) => v is null ? "n/a" : $"{v.Value:0.#}%";
    }
}
