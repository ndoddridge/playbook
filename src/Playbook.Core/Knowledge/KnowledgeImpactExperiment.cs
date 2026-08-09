using Playbook.Core.Replay;

namespace Playbook.Core.Knowledge;

/// <summary>Whether prediction engines consume knowledge-group transforms.</summary>
public enum KnowledgeMode
{
    /// <summary>
    /// Identity passthrough (default). Assembled shared knowledge is unchanged.
    /// Preserves frozen Projection/Confidence/Policy benchmarks outside experiments.
    /// </summary>
    Passthrough = 0,

    /// <summary>
    /// Experiment control: projections + confidence + engine baseline only.
    /// Contextual knowledge groups are stripped / not applied.
    /// </summary>
    Baseline = 1,

    /// <summary>Experiment: explicit knowledge-group transforms are applied.</summary>
    Enhanced = 2
}

/// <summary>Independently ablatable knowledge groups with reliable historical coverage.</summary>
[Flags]
public enum KnowledgeImpactGroup
{
    None = 0,

    /// <summary>Player recent production / form (not team RecentForm aspect).</summary>
    RecentForm = 1,

    /// <summary>Usage + opportunity scores.</summary>
    Usage = 2,

    /// <summary>Role note + health/injury signals.</summary>
    RoleHealth = 4,

    /// <summary>
    /// Matchup/team context — insufficient historical coverage in V1; not enabled.
    /// </summary>
    Matchup = 8,

    AllSupported = RecentForm | Usage | RoleHealth
}

/// <summary>
/// Frozen Knowledge Impact Experiment V1 configuration.
/// Selected on development seasons only; 2024 must not influence these values.
/// </summary>
public static class FrozenKnowledgeImpactExperimentV1
{
    public const string ExperimentId = "knowledge-impact-experiment-v1";

    public static readonly IReadOnlyList<int> DevelopmentSeasons = [2015, 2018, 2021];

    public const int HoldoutSeason = 2024;

    /// <summary>
    /// Groups enabled in the frozen Enhanced configuration.
    /// Selected after development ablation; Matchup excluded (insufficient coverage).
    /// </summary>
    public const KnowledgeImpactGroup FrozenEnhancedGroups = KnowledgeImpactGroup.Usage;

    public const string SelectionSummary =
        "Development LOOCV ablation on {2015,2018,2021} under Projection V2 + Confidence V2 + Policy Off; " +
        "compared Baseline vs RecentForm / Usage / RoleHealth independently; " +
        "mean val Δ: RecentForm +0.0 (neutral), Usage +38.7 (positive), RoleHealth −10.0 (negative). " +
        "Froze Usage only. Matchup skipped (team/matchup aspects unavailable historically). 2024 was not used.";

    /// <summary>Bounded opportunity adjustment from recent-form transform (points on 0–100 score).</summary>
    public const int RecentFormOpportunityDelta = 6;

    /// <summary>Recent-production thresholds for form transform.</summary>
    public const int RecentFormHighThreshold = 65;

    public const int RecentFormLowThreshold = 35;

    /// <summary>Bounded opportunity adjustment from role transform.</summary>
    public const int RoleOpportunityDelta = 4;
}

/// <summary>Pre-registered success criteria before official 2024 holdout.</summary>
public static class KnowledgeImpactSuccessCriteria
{
    public const double MinDevMeanTotalValueImprovement = 15.0;

    public const double MinHoldoutTotalValueImprovement = 20.0;

    /// <summary>Minimum share of graded decisions that must change vs baseline to count as material.</summary>
    public const double MinHoldoutChangeRate = 0.05;

    public const string Text =
        "SUCCESS for a knowledge group (or frozen Enhanced set) requires: " +
        "(1) Dev LOOCV mean total decision value improves by >=15 vs Baseline; " +
        "(2) Official 2024 total decision value improves by >=20 vs Baseline; " +
        "(3) >=5% of holdout graded decisions change vs Baseline; " +
        "(4) Projection V2, Confidence V2, and Decision Policy V1 unchanged. " +
        "Tiny fluctuations are NOT improvement.";
}

/// <summary>One ablation / mode evaluation scope.</summary>
public sealed class KnowledgeImpactScopeMetrics
{
    public required string Label { get; init; }

    public required KnowledgeMode Mode { get; init; }

    public required KnowledgeImpactGroup Groups { get; init; }

    public required int GradedDecisions { get; init; }

    public required int DecisionsChangedVsBaseline { get; init; }

    public required double? ChangeRatePercent { get; init; }

    public required double? AccuracyPercent { get; init; }

    public required double? AverageDecisionValue { get; init; }

    public required double? TotalDecisionValue { get; init; }

    public required double? WorstDecisionCost { get; init; }

    public required double? ProjectionMae { get; init; }

    public required double? ProjectionBias { get; init; }
}

/// <summary>Per-group ablation row.</summary>
public sealed class KnowledgeImpactAblationRow
{
    public required string GroupName { get; init; }

    public required KnowledgeImpactGroup Group { get; init; }

    public required string CoverageNote { get; init; }

    public required KnowledgeImpactScopeMetrics Development { get; init; }

    public required KnowledgeImpactScopeMetrics? Holdout { get; init; }

    public required ProjectionExperimentVerdict Verdict { get; init; }

    public required string VerdictRationale { get; init; }
}

/// <summary>Full Experiment report.</summary>
public sealed class KnowledgeImpactExperimentReport
{
    public required DateTimeOffset GeneratedAt { get; init; }

    public required string Hypothesis { get; init; }

    public required string SuccessCriteriaText { get; init; }

    public required IReadOnlyList<int> DevelopmentSeasons { get; init; }

    public required int HoldoutSeason { get; init; }

    public required bool UsedHoldoutDuringFitting { get; init; }

    public required bool ProjectionV2Unchanged { get; init; }

    public required bool ConfidenceV2Unchanged { get; init; }

    public required bool DecisionPolicyV1Unchanged { get; init; }

    public required IReadOnlyList<string> AvailableSignalNotes { get; init; }

    public required IReadOnlyList<string> DataCoverageNotes { get; init; }

    public required IReadOnlyList<string> LooFoldSummaries { get; init; }

    public required KnowledgeImpactGroup FrozenGroups { get; init; }

    public required KnowledgeImpactScopeMetrics DevelopmentBaseline { get; init; }

    public required KnowledgeImpactScopeMetrics DevelopmentEnhanced { get; init; }

    public required KnowledgeImpactScopeMetrics HoldoutBaseline { get; init; }

    public required KnowledgeImpactScopeMetrics HoldoutEnhanced { get; init; }

    public required IReadOnlyList<KnowledgeImpactAblationRow> AblationRows { get; init; }

    public required IReadOnlyList<string> FailureAnalysisNotes { get; init; }

    public required string QuickPicksEvaluationNote { get; init; }

    public required ProjectionExperimentVerdict Verdict { get; init; }

    public required string VerdictRationale { get; init; }

    public string ToReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("KNOWLEDGE IMPACT EXPERIMENT — V1");
        sb.AppendLine($"Generated: {GeneratedAt:u}");
        sb.AppendLine($"Holdout used during fitting: {UsedHoldoutDuringFitting}");
        sb.AppendLine($"Projection V2 unchanged: {ProjectionV2Unchanged}");
        sb.AppendLine($"Confidence V2 unchanged: {ConfidenceV2Unchanged}");
        sb.AppendLine($"Decision Policy V1 unchanged: {DecisionPolicyV1Unchanged}");
        sb.AppendLine($"Frozen Enhanced groups: {FrozenGroups}");
        sb.AppendLine();
        sb.AppendLine("HYPOTHESIS");
        sb.AppendLine($"  {Hypothesis}");
        sb.AppendLine();
        sb.AppendLine("SUCCESS CRITERIA");
        sb.AppendLine($"  {SuccessCriteriaText}");
        sb.AppendLine();
        sb.AppendLine("AVAILABLE SIGNALS");
        foreach (var n in AvailableSignalNotes)
        {
            sb.AppendLine($"  - {n}");
        }

        sb.AppendLine();
        sb.AppendLine("DATA COVERAGE");
        foreach (var n in DataCoverageNotes)
        {
            sb.AppendLine($"  - {n}");
        }

        sb.AppendLine();
        sb.AppendLine("DEVELOPMENT LOOCV / ABLATION");
        foreach (var fold in LooFoldSummaries)
        {
            sb.AppendLine($"  {fold}");
        }

        AppendScope(sb, "DEV BASELINE", DevelopmentBaseline);
        AppendScope(sb, "DEV ENHANCED (frozen groups)", DevelopmentEnhanced);
        foreach (var row in AblationRows)
        {
            AppendScope(sb, $"DEV ABLATION {row.GroupName}", row.Development);
            sb.AppendLine($"    verdict={row.Verdict}: {row.VerdictRationale}");
        }

        sb.AppendLine();
        sb.AppendLine("OFFICIAL HOLDOUT 2024");
        AppendScope(sb, "HOLDOUT BASELINE", HoldoutBaseline);
        AppendScope(sb, "HOLDOUT ENHANCED", HoldoutEnhanced);
        sb.AppendLine(
            $"  Δ total decision value (enh-base): " +
            $"{Fmt((HoldoutEnhanced.TotalDecisionValue ?? 0) - (HoldoutBaseline.TotalDecisionValue ?? 0))}");
        sb.AppendLine(
            $"  Change rate: {HoldoutEnhanced.ChangeRatePercent:0.#}% " +
            $"({HoldoutEnhanced.DecisionsChangedVsBaseline}/{HoldoutBaseline.GradedDecisions})");

        sb.AppendLine();
        sb.AppendLine("QUICK PICKS");
        sb.AppendLine($"  {QuickPicksEvaluationNote}");
        sb.AppendLine();
        sb.AppendLine("FAILURE ANALYSIS");
        foreach (var n in FailureAnalysisNotes)
        {
            sb.AppendLine($"  - {n}");
        }

        sb.AppendLine();
        sb.AppendLine("VERDICT");
        sb.AppendLine($"  {Verdict}");
        sb.AppendLine($"  {VerdictRationale}");
        return sb.ToString();

        static void AppendScope(System.Text.StringBuilder target, string title, KnowledgeImpactScopeMetrics m)
        {
            target.AppendLine(
                $"  {title}: mode={m.Mode} groups={m.Groups} n={m.GradedDecisions} " +
                $"changed={m.DecisionsChangedVsBaseline} ({FmtPct(m.ChangeRatePercent)}) " +
                $"acc={FmtPct(m.AccuracyPercent)} avgVal={Fmt(m.AverageDecisionValue)} " +
                $"tot={Fmt(m.TotalDecisionValue)} worst={Fmt(m.WorstDecisionCost)} " +
                $"mae={Fmt(m.ProjectionMae)} bias={Fmt(m.ProjectionBias)}");
        }

        static string Fmt(double? v) => v is null ? "n/a" : v.Value.ToString("0.00");
        static string FmtPct(double? v) => v is null ? "n/a" : $"{v.Value:0.#}%";
    }
}
