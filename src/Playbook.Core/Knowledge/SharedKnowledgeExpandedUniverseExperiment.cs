using System.Text;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;

namespace Playbook.Core.Knowledge;

/// <summary>
/// Frozen Shared-Knowledge × Expanded-Universe Experiment V1.
///
/// Narrow question: does assembled shared knowledge (production Passthrough —
/// Opportunity/Usage/Role/Health/form as DecisionEngine already consumes them)
/// improve Start/Sit and Quick Picks quality vs the stripped Baseline control
/// when the evaluation surface is ExpandedSkillUniverse?
///
/// Does NOT re-enable Usage / RoleHealth / RecentForm / RecentFormThinMargin /
/// DataSufficiencyTrust Enhanced transforms. ActiveGroups stay None.
/// No tunable parameters — freeze is mode+universe only before the 2024 holdout.
/// </summary>
public static class FrozenSharedKnowledgeExpandedUniverseExperimentV1
{
    public const string ExperimentId = "shared-knowledge-expanded-universe-v1";

    public static readonly IReadOnlyList<int> DevelopmentSeasons = [2015, 2018, 2021];

    public const int HoldoutSeason = 2024;

    public const HistoricalCandidateUniverse CandidateUniverse =
        HistoricalCandidateUniverse.ExpandedSkillUniverse;

    /// <summary>Control mode — contextual knowledge stripped.</summary>
    public const KnowledgeMode ControlMode = KnowledgeMode.Baseline;

    /// <summary>Treatment mode — raw assembled shared knowledge (production behavior).</summary>
    public const KnowledgeMode TreatmentMode = KnowledgeMode.Passthrough;

    public const KnowledgeImpactGroup ActiveGroups = KnowledgeImpactGroup.None;

    public const string Hypothesis =
        "On the expanded ACT skill evaluation surface, assembled shared knowledge as " +
        "production already consumes it (KnowledgeMode.Passthrough) improves Start/Sit " +
        "decision value versus the stripped Baseline control — without re-enabling any " +
        "previously rejected Enhanced transforms.";

    public const string MappingSummary =
        "Control=Baseline (strip Opportunity/Usage/RecentProduction/Role/Health; KnowledgeConfidence−15). " +
        "Treatment=Passthrough (identity; DecisionEngine reads composed Opportunity/Usage/Health/Role/form). " +
        "ActiveGroups=None. CandidateUniverse=ExpandedSkillUniverse. " +
        "Rejected transforms stay off. Frozen 2018 LabRoster benchmark path untouched. " +
        "Quick Picks: Baseline ranking vs knowledge-attached Passthrough applicator " +
        "(expected ranking identity; coverage/confidence reported).";
}

/// <summary>Knowledge usability coverage for one evaluation scope.</summary>
public sealed class SharedKnowledgeCoverageStats
{
    public required int CandidatePlayerWeeks { get; init; }

    public required int WithValidProjection { get; init; }

    public required int WithUsableSharedKnowledge { get; init; }

    public required int ProjectionOnlyOrUnknown { get; init; }

    public required int WithOpportunity { get; init; }

    public required int WithUsage { get; init; }

    public required int WithRecentProduction { get; init; }

    public required int WithRole { get; init; }

    public required int WithNonDefaultHealth { get; init; }

    public required int WithLimitedHistory { get; init; }

    public required int WithInsufficientHistory { get; init; }

    public required int WithSufficientHistory { get; init; }

    public required double UsableKnowledgeRatePercent { get; init; }

    public required IReadOnlyList<string> UnavailabilityNotes { get; init; }
}

/// <summary>Per-category Start/Sit slice (Baseline vs Passthrough).</summary>
public sealed class SharedKnowledgeCategorySlice
{
    public required string Category { get; init; }

    public required int BaselineGraded { get; init; }

    public required int TreatmentGraded { get; init; }

    public required double? BaselineTotalDecisionValue { get; init; }

    public required double? TreatmentTotalDecisionValue { get; init; }

    public required double? DeltaTotalDecisionValue { get; init; }

    public required double? BaselineAccuracyPercent { get; init; }

    public required double? TreatmentAccuracyPercent { get; init; }
}

/// <summary>Quick Picks scope metrics for this experiment.</summary>
public sealed class SharedKnowledgeQuickPickScope
{
    public required string Label { get; init; }

    public required int PredictionsEvaluated { get; init; }

    public required double MeanAbsoluteError { get; init; }

    public required double Top5HitRate { get; init; }

    public required double Top10HitRate { get; init; }

    public required double MeanRankAbsoluteError { get; init; }

    public required double? AverageConfidence { get; init; }

    public required int RanksChangedVsControl { get; init; }

    public required double? RankChangeRatePercent { get; init; }

    public required int KnowledgeAttachedCount { get; init; }
}

/// <summary>Confidence bucket row (measurement only).</summary>
public sealed class SharedKnowledgeConfidenceBucketRow
{
    public required string Label { get; init; }

    public required int DecisionCount { get; init; }

    public required int GradedCount { get; init; }

    public required double? SuccessRatePercent { get; init; }

    public required double? AverageDecisionValue { get; init; }
}

/// <summary>Full experiment report.</summary>
public sealed class SharedKnowledgeExpandedUniverseExperimentReport
{
    public required DateTimeOffset GeneratedAt { get; init; }

    public required string ExperimentId { get; init; }

    public required string Hypothesis { get; init; }

    public required string SuccessCriteriaText { get; init; }

    public required IReadOnlyList<int> DevelopmentSeasons { get; init; }

    public required int HoldoutSeason { get; init; }

    public required HistoricalCandidateUniverse CandidateUniverse { get; init; }

    public required bool UsedHoldoutDuringFitting { get; init; }

    public required bool ProjectionV2Unchanged { get; init; }

    public required bool ConfidenceV2Unchanged { get; init; }

    public required bool DecisionPolicyV1Unchanged { get; init; }

    public required bool RejectedTransformsRemainDisabled { get; init; }

    public required IReadOnlyList<string> LooFoldSummaries { get; init; }

    public required KnowledgeImpactScopeMetrics DevelopmentBaseline { get; init; }

    public required KnowledgeImpactScopeMetrics DevelopmentTreatment { get; init; }

    public required KnowledgeImpactScopeMetrics HoldoutBaseline { get; init; }

    public required KnowledgeImpactScopeMetrics HoldoutTreatment { get; init; }

    public required int DevelopmentStartSitCandidates { get; init; }

    public required int HoldoutStartSitCandidates { get; init; }

    public required SharedKnowledgeCoverageStats DevelopmentCoverage { get; init; }

    public required SharedKnowledgeCoverageStats HoldoutCoverage { get; init; }

    public required IReadOnlyList<SharedKnowledgeCategorySlice> DevelopmentCategorySlices { get; init; }

    public required IReadOnlyList<SharedKnowledgeCategorySlice> HoldoutCategorySlices { get; init; }

    public required IReadOnlyList<SharedKnowledgeConfidenceBucketRow> HoldoutBaselineConfidenceBuckets { get; init; }

    public required IReadOnlyList<SharedKnowledgeConfidenceBucketRow> HoldoutTreatmentConfidenceBuckets { get; init; }

    public required SharedKnowledgeQuickPickScope DevelopmentQuickPicksBaseline { get; init; }

    public required SharedKnowledgeQuickPickScope DevelopmentQuickPicksTreatment { get; init; }

    public required SharedKnowledgeQuickPickScope HoldoutQuickPicksBaseline { get; init; }

    public required SharedKnowledgeQuickPickScope HoldoutQuickPicksTreatment { get; init; }

    public required IReadOnlyList<string> FailureAnalysisNotes { get; init; }

    public required ProjectionExperimentVerdict Verdict { get; init; }

    public required string VerdictRationale { get; init; }

    public string ToReportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("SHARED KNOWLEDGE × EXPANDED UNIVERSE EXPERIMENT — V1");
        sb.AppendLine($"ExperimentId: {ExperimentId}");
        sb.AppendLine($"Generated: {GeneratedAt:u}");
        sb.AppendLine($"CandidateUniverse: {CandidateUniverse}");
        sb.AppendLine($"Control={FrozenSharedKnowledgeExpandedUniverseExperimentV1.ControlMode} " +
                      $"Treatment={FrozenSharedKnowledgeExpandedUniverseExperimentV1.TreatmentMode} " +
                      $"Groups={FrozenSharedKnowledgeExpandedUniverseExperimentV1.ActiveGroups}");
        sb.AppendLine($"Holdout used during fitting: {UsedHoldoutDuringFitting}");
        sb.AppendLine($"Projection V2 unchanged: {ProjectionV2Unchanged}");
        sb.AppendLine($"Confidence V2 unchanged: {ConfidenceV2Unchanged}");
        sb.AppendLine($"Decision Policy V1 unchanged: {DecisionPolicyV1Unchanged}");
        sb.AppendLine($"Rejected transforms disabled: {RejectedTransformsRemainDisabled}");
        sb.AppendLine();
        sb.AppendLine("HYPOTHESIS");
        sb.AppendLine($"  {Hypothesis}");
        sb.AppendLine();
        sb.AppendLine("SUCCESS CRITERIA");
        sb.AppendLine($"  {SuccessCriteriaText}");
        sb.AppendLine();
        sb.AppendLine("DEVELOPMENT FOLDS (informational — no parameter selection)");
        foreach (var fold in LooFoldSummaries)
        {
            sb.AppendLine($"  {fold}");
        }

        AppendScope(sb, "DEV BASELINE (control)", DevelopmentBaseline);
        AppendScope(sb, "DEV PASSTHROUGH (treatment)", DevelopmentTreatment);
        sb.AppendLine($"  DEV Start/Sit candidates (player-weeks on roster): {DevelopmentStartSitCandidates}");
        AppendCoverage(sb, "DEV knowledge coverage", DevelopmentCoverage);
        foreach (var slice in DevelopmentCategorySlices)
        {
            AppendCategory(sb, slice);
        }

        sb.AppendLine();
        sb.AppendLine("OFFICIAL HOLDOUT 2024");
        AppendScope(sb, "HOLDOUT BASELINE", HoldoutBaseline);
        AppendScope(sb, "HOLDOUT PASSTHROUGH", HoldoutTreatment);
        sb.AppendLine(
            $"  Δ total decision value (treatment−control): " +
            $"{Fmt((HoldoutTreatment.TotalDecisionValue ?? 0) - (HoldoutBaseline.TotalDecisionValue ?? 0))}");
        sb.AppendLine(
            $"  Change rate: {HoldoutTreatment.ChangeRatePercent:0.#}% " +
            $"({HoldoutTreatment.DecisionsChangedVsBaseline}/{HoldoutBaseline.GradedDecisions})");
        sb.AppendLine($"  HOLDOUT Start/Sit candidates: {HoldoutStartSitCandidates}");
        AppendCoverage(sb, "HOLDOUT knowledge coverage", HoldoutCoverage);
        foreach (var slice in HoldoutCategorySlices)
        {
            AppendCategory(sb, slice);
        }

        sb.AppendLine();
        sb.AppendLine("HOLDOUT CONFIDENCE BUCKETS (Baseline)");
        foreach (var b in HoldoutBaselineConfidenceBuckets)
        {
            sb.AppendLine(
                $"  {b.Label}: n={b.DecisionCount} graded={b.GradedCount} " +
                $"success={FmtPct(b.SuccessRatePercent)} avgVal={Fmt(b.AverageDecisionValue)}");
        }

        sb.AppendLine("HOLDOUT CONFIDENCE BUCKETS (Passthrough)");
        foreach (var b in HoldoutTreatmentConfidenceBuckets)
        {
            sb.AppendLine(
                $"  {b.Label}: n={b.DecisionCount} graded={b.GradedCount} " +
                $"success={FmtPct(b.SuccessRatePercent)} avgVal={Fmt(b.AverageDecisionValue)}");
        }

        sb.AppendLine();
        sb.AppendLine("QUICK PICKS (ExpandedSkillUniverse)");
        AppendQp(sb, DevelopmentQuickPicksBaseline);
        AppendQp(sb, DevelopmentQuickPicksTreatment);
        AppendQp(sb, HoldoutQuickPicksBaseline);
        AppendQp(sb, HoldoutQuickPicksTreatment);

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
    }

    private static void AppendScope(StringBuilder sb, string title, KnowledgeImpactScopeMetrics m) =>
        sb.AppendLine(
            $"  {title}: mode={m.Mode} groups={m.Groups} n={m.GradedDecisions} " +
            $"changed={m.DecisionsChangedVsBaseline} ({FmtPct(m.ChangeRatePercent)}) " +
            $"acc={FmtPct(m.AccuracyPercent)} avgVal={Fmt(m.AverageDecisionValue)} " +
            $"tot={Fmt(m.TotalDecisionValue)} worst={Fmt(m.WorstDecisionCost)} " +
            $"mae={Fmt(m.ProjectionMae)} bias={Fmt(m.ProjectionBias)}");

    private static void AppendCoverage(StringBuilder sb, string title, SharedKnowledgeCoverageStats c)
    {
        sb.AppendLine(
            $"  {title}: candidates={c.CandidatePlayerWeeks} usable={c.WithUsableSharedKnowledge} " +
            $"({c.UsableKnowledgeRatePercent:0.#}%) projectionOnly/unknown={c.ProjectionOnlyOrUnknown} " +
            $"opp={c.WithOpportunity} usage={c.WithUsage} form={c.WithRecentProduction} " +
            $"role={c.WithRole} health≠Healthy={c.WithNonDefaultHealth} " +
            $"suff={c.WithSufficientHistory} lim={c.WithLimitedHistory} insuff={c.WithInsufficientHistory}");
        foreach (var n in c.UnavailabilityNotes)
        {
            sb.AppendLine($"    · {n}");
        }
    }

    private static void AppendCategory(StringBuilder sb, SharedKnowledgeCategorySlice s) =>
        sb.AppendLine(
            $"  category[{s.Category}]: baseN={s.BaselineGraded} treatN={s.TreatmentGraded} " +
            $"baseTot={Fmt(s.BaselineTotalDecisionValue)} treatTot={Fmt(s.TreatmentTotalDecisionValue)} " +
            $"Δ={Fmt(s.DeltaTotalDecisionValue)} baseAcc={FmtPct(s.BaselineAccuracyPercent)} " +
            $"treatAcc={FmtPct(s.TreatmentAccuracyPercent)}");

    private static void AppendQp(StringBuilder sb, SharedKnowledgeQuickPickScope q) =>
        sb.AppendLine(
            $"  {q.Label}: preds={q.PredictionsEvaluated} MAE={q.MeanAbsoluteError:0.000} " +
            $"Top5={q.Top5HitRate:0.0}% Top10={q.Top10HitRate:0.0}% " +
            $"rankMAE={q.MeanRankAbsoluteError:0.00} conf={Fmt(q.AverageConfidence)} " +
            $"ranksChanged={q.RanksChangedVsControl} ({FmtPct(q.RankChangeRatePercent)}) " +
            $"knowledgeAttached={q.KnowledgeAttachedCount}");

    private static string Fmt(double? v) => v is null ? "n/a" : v.Value.ToString("0.00");

    private static string FmtPct(double? v) => v is null ? "n/a" : $"{v.Value:0.#}%";
}
