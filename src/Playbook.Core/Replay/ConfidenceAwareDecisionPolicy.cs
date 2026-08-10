using Playbook.Core.Decisions;
using Playbook.Core.Players;

namespace Playbook.Core.Replay;

/// <summary>Trust presentation label for a Start/Sit recommendation.</summary>
public enum DecisionTrustLabel
{
    Unspecified = 0,
    HighTrust = 1,
    LowTrust = 2,
    Suppressed = 3
}

/// <summary>Which confidence-aware decision policy is active.</summary>
public enum ConfidenceAwareDecisionPolicyMode
{
    /// <summary>Control: existing decision rules only.</summary>
    Off = 0,

    /// <summary>Experiment policy (frozen after development LOOCV).</summary>
    On = 1
}

/// <summary>Policy kind explored on development data.</summary>
public static class DecisionPolicyKinds
{
    public const string SuppressStart = "SuppressStart";
    public const string SuppressStartAndSit = "SuppressStartAndSit";
    public const string SwapStart = "SwapStart";
}

/// <summary>Executable policy definition (candidate or frozen).</summary>
public sealed class ConfidenceAwareDecisionPolicyDefinition
{
    public required string CandidateId { get; init; }

    public required string Kind { get; init; }

    public required int Threshold { get; init; }

    public required double Margin { get; init; }

    /// <summary>Calibrated confidence at/above this is labeled HIGH TRUST.</summary>
    public required int HighTrustMin { get; init; }

    public required string Description { get; init; }

    public static ConfidenceAwareDecisionPolicyDefinition FromFrozen() =>
        new()
        {
            CandidateId = FrozenConfidenceAwareDecisionPolicyV1.PolicyId,
            Kind = FrozenConfidenceAwareDecisionPolicyV1.Kind,
            Threshold = FrozenConfidenceAwareDecisionPolicyV1.MaxCalibratedConfidenceToSuppressStart,
            Margin = FrozenConfidenceAwareDecisionPolicyV1.MaxDecisionValueMarginToSuppress,
            HighTrustMin = FrozenConfidenceAwareDecisionPolicyV1.HighTrustMinCalibratedConfidence,
            Description = FrozenConfidenceAwareDecisionPolicyV1.RuleSummary
        };
}

/// <summary>
/// Frozen confidence-aware decision policy (Experiment 3).
/// Selected via leave-one-season-out on development seasons only under Projection V2.
/// 2024 must never influence these values.
/// </summary>
public static class FrozenConfidenceAwareDecisionPolicyV1
{
    public const string PolicyId = "confidence-aware-decision-policy-v1";

    public static readonly IReadOnlyList<int> DevelopmentSeasons = [2015, 2018, 2021];

    public const int HoldoutSeason = 2024;

    /// <summary>Policy kind — frozen after development LOOCV selection.</summary>
    public const string Kind = DecisionPolicyKinds.SuppressStartAndSit;

    /// <summary>
    /// Suppress assertive Start/Sit recommendations when calibrated confidence is at or below this threshold.
    /// </summary>
    public const int MaxCalibratedConfidenceToSuppressStart = 45;

    /// <summary>
    /// Only suppress when the DecisionValue margin over the next alternative is below this
    /// (points). Strong edges are kept even at low calibrated confidence.
    /// </summary>
    public const double MaxDecisionValueMarginToSuppress = 6.0;

    public const int HighTrustMinCalibratedConfidence = 60;

    public const string RuleSummary =
        "IF recommendation is Start or Sit " +
        "AND calibratedConfidence <= 45 " +
        "AND decisionValue margin vs best alternative < 6.0 " +
        "THEN suppress the recommendation (do not emit it; abstain from that graded decision). " +
        "Otherwise keep recommendation and label HIGH/LOW TRUST by calibratedConfidence >= 60.";

    /// <summary>Confirmed after development LOOCV; must not mention 2024 fitting.</summary>
    public const string SelectionSummary =
        "Leave-one-season-out on {2015,2018,2021} under Projection V2 + Confidence V2; " +
        "compared SuppressStart / SuppressStartAndSit / SwapStart over thresholds " +
        "{40,45,50,55,60,65} and margins {3,6}; selected SuppressStartAndSit@t45-m6 by highest " +
        "mean validation total-decision-value delta (+41.0) with mean retention 53% (>=50%). " +
        "Tied with t50/t55-m6; lowest threshold kept. 2024 was not used.";
}

/// <summary>Pre-registered success criteria before official 2024 holdout.</summary>
public static class ConfidenceAwareDecisionPolicySuccessCriteria
{
    /// <summary>Minimum absolute total-decision-value improvement on development LOOCV mean.</summary>
    public const double MinDevMeanTotalValueImprovement = 15.0;

    /// <summary>Minimum absolute total-decision-value improvement on official holdout.</summary>
    public const double MinHoldoutTotalValueImprovement = 20.0;

    /// <summary>Must retain at least this fraction of control graded decisions on holdout.</summary>
    public const double MinHoldoutDecisionRetention = 0.50;

    public const string Text =
        "SUCCESS requires: (1) Dev LOOCV mean total decision value improves by >=15 vs control; " +
        "(2) Official 2024 total decision value improves by >=20 vs control; " +
        "(3) Holdout retains >=50% of control graded decisions (no abstention gaming); " +
        "(4) Projection V2 and Confidence V2 unchanged. Tiny fluctuations are NOT improvement.";
}

/// <summary>One graded decision observation for offline policy search.</summary>
public sealed class DecisionPolicyObservation
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required Guid DecisionId { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Position Position { get; init; }

    public required DecisionRecommendation Recommendation { get; init; }

    public required int RawConfidence { get; init; }

    public required int CalibratedConfidence { get; init; }

    public required double DecisionValue { get; init; }

    /// <summary>DecisionValue of recommended player minus next alternative (Start); Sit uses same margin field when known.</summary>
    public required double? DecisionValueMargin { get; init; }

    public required double? RecommendationMargin { get; init; }

    public required double? ActualDecisionDifferential { get; init; }

    public required bool? WasCorrect { get; init; }

    public int? OpportunityScore { get; init; }

    public int? UsageScore { get; init; }

    public int? RecentProductionScore { get; init; }
}

/// <summary>One candidate policy configuration explored on development data only.</summary>
public sealed class DecisionPolicyCandidate
{
    public required string CandidateId { get; init; }

    public required string Kind { get; init; }

    public required int Threshold { get; init; }

    public required double Margin { get; init; }

    public required string Description { get; init; }

    public ConfidenceAwareDecisionPolicyDefinition ToDefinition() =>
        new()
        {
            CandidateId = CandidateId,
            Kind = Kind,
            Threshold = Threshold,
            Margin = Margin,
            HighTrustMin = FrozenConfidenceAwareDecisionPolicyV1.HighTrustMinCalibratedConfidence,
            Description = Description
        };
}

/// <summary>Per-scope decision metrics for control vs experiment.</summary>
public sealed class DecisionPolicyScopeMetrics
{
    public required string Label { get; init; }

    public required int GradedDecisions { get; init; }

    public required int Opportunities { get; init; }

    public required int SuppressedStarts { get; init; }

    public required int SuppressedSits { get; init; }

    public required int SwappedStarts { get; init; }

    public required int LowTrustLabeled { get; init; }

    public required double? AccuracyPercent { get; init; }

    public required double? AverageDecisionValue { get; init; }

    public required double? TotalDecisionValue { get; init; }

    public required double? WorstDecisionCost { get; init; }

    public required double? BestDecisionValue { get; init; }

    public required double? SuppressedWouldHaveBeenTotalValue { get; init; }

    public required IReadOnlyDictionary<string, int> ConfidenceDistribution { get; init; }
}

/// <summary>Full Experiment 3 report.</summary>
public sealed class ConfidenceAwareDecisionPolicyExperimentReport
{
    public required DateTimeOffset GeneratedAt { get; init; }

    public required string Hypothesis { get; init; }

    public required string ControlDescription { get; init; }

    public required string ExperimentalPolicyDescription { get; init; }

    public required string SuccessCriteriaText { get; init; }

    public required IReadOnlyList<int> DevelopmentSeasons { get; init; }

    public required int HoldoutSeason { get; init; }

    public required bool UsedHoldoutDuringFitting { get; init; }

    public required bool ProjectionV2Unchanged { get; init; }

    public required bool ConfidenceV2Unchanged { get; init; }

    public required IReadOnlyList<string> LooFoldSummaries { get; init; }

    public required string SelectedCandidateId { get; init; }

    public required DecisionPolicyScopeMetrics DevelopmentControl { get; init; }

    public required DecisionPolicyScopeMetrics DevelopmentExperiment { get; init; }

    public required DecisionPolicyScopeMetrics HoldoutControl { get; init; }

    public required DecisionPolicyScopeMetrics HoldoutExperiment { get; init; }

    public required IReadOnlyList<string> FailureAnalysisNotes { get; init; }

    public required ProjectionExperimentVerdict Verdict { get; init; }

    public required string VerdictRationale { get; init; }

    public string ToReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("CONFIDENCE-AWARE DECISION POLICY EXPERIMENT — V1");
        sb.AppendLine($"Generated: {GeneratedAt:u}");
        sb.AppendLine($"Holdout used during fitting: {UsedHoldoutDuringFitting}");
        sb.AppendLine($"Projection V2 unchanged: {ProjectionV2Unchanged}");
        sb.AppendLine($"Confidence V2 unchanged: {ConfidenceV2Unchanged}");
        sb.AppendLine();
        sb.AppendLine("HYPOTHESIS");
        sb.AppendLine($"  {Hypothesis}");
        sb.AppendLine();
        sb.AppendLine("SUCCESS CRITERIA (pre-registered)");
        sb.AppendLine($"  {SuccessCriteriaText}");
        sb.AppendLine();
        sb.AppendLine("CONTROL");
        sb.AppendLine($"  {ControlDescription}");
        sb.AppendLine();
        sb.AppendLine("EXPERIMENTAL POLICY (frozen)");
        sb.AppendLine($"  Candidate: {SelectedCandidateId}");
        sb.AppendLine($"  {ExperimentalPolicyDescription}");
        sb.AppendLine($"  Rule: {FrozenConfidenceAwareDecisionPolicyV1.RuleSummary}");
        sb.AppendLine();
        sb.AppendLine("DEVELOPMENT LOOCV");
        foreach (var fold in LooFoldSummaries)
        {
            sb.AppendLine($"  {fold}");
        }

        AppendScope(sb, "DEV CONTROL", DevelopmentControl);
        AppendScope(sb, "DEV EXPERIMENT", DevelopmentExperiment);
        sb.AppendLine();
        sb.AppendLine("OFFICIAL HOLDOUT 2024");
        AppendScope(sb, "HOLDOUT CONTROL", HoldoutControl);
        AppendScope(sb, "HOLDOUT EXPERIMENT", HoldoutExperiment);
        sb.AppendLine(
            $"  Δ total decision value (exp-control): " +
            $"{Fmt((HoldoutExperiment.TotalDecisionValue ?? 0) - (HoldoutControl.TotalDecisionValue ?? 0))}");
        sb.AppendLine(
            $"  Retention: {HoldoutExperiment.GradedDecisions}/{HoldoutControl.GradedDecisions}");
        sb.AppendLine();
        sb.AppendLine("FAILURE ANALYSIS");
        foreach (var note in FailureAnalysisNotes)
        {
            sb.AppendLine($"  - {note}");
        }

        sb.AppendLine();
        sb.AppendLine("VERDICT");
        sb.AppendLine($"  {Verdict}");
        sb.AppendLine($"  {VerdictRationale}");
        return sb.ToString();

        static void AppendScope(System.Text.StringBuilder target, string title, DecisionPolicyScopeMetrics m)
        {
            target.AppendLine(
                $"  {title}: n={m.GradedDecisions}/{m.Opportunities} " +
                $"supStart={m.SuppressedStarts} supSit={m.SuppressedSits} swap={m.SwappedStarts} " +
                $"lowTrust={m.LowTrustLabeled} " +
                $"acc={FmtPct(m.AccuracyPercent)} avgVal={Fmt(m.AverageDecisionValue)} " +
                $"tot={Fmt(m.TotalDecisionValue)} worst={Fmt(m.WorstDecisionCost)} " +
                $"best={Fmt(m.BestDecisionValue)} suppressedWouldHave={Fmt(m.SuppressedWouldHaveBeenTotalValue)}");
            target.AppendLine(
                $"    confDist: {string.Join(", ", m.ConfidenceDistribution.Select(kv => $"{kv.Key}={kv.Value}"))}");
        }

        static string Fmt(double? v) => v is null ? "n/a" : v.Value.ToString("0.00");
        static string FmtPct(double? v) => v is null ? "n/a" : $"{v.Value:0.#}%";
    }
}
