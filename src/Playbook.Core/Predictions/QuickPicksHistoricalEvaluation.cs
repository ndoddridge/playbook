using System.Text;
using Playbook.Core.Knowledge;
using Playbook.Core.Players;

namespace Playbook.Core.Predictions;

/// <summary>
/// Quick Picks evaluation mode for historical replay.
/// Baseline = current QP semantics without knowledge influence.
/// Enhanced = may consume SharedKnowledge under the frozen allowed-group policy.
/// </summary>
public enum QuickPickMode
{
    Baseline = 0,
    Enhanced = 1
}

/// <summary>
/// Frozen Quick Picks Historical Evaluation V1 configuration.
/// Development seasons may inform harness discipline; 2024 must not influence formulas,
/// thresholds, signal selection, weights, or policy decisions.
/// </summary>
public static class FrozenQuickPicksHistoricalEvaluationV1
{
    public const string EvaluationId = "quick-picks-historical-evaluation-v1";

    public static readonly IReadOnlyList<int> DevelopmentSeasons = [2015, 2018, 2021];

    public const int HoldoutSeason = 2024;

    /// <summary>
    /// Knowledge groups allowed for Enhanced Quick Picks in V1.
    /// Knowledge Impact Experiment V1 rejected Usage / RoleHealth / RecentForm for production;
    /// Matchup lacks historical coverage. Enhanced therefore starts as observational identity.
    /// </summary>
    public const KnowledgeImpactGroup AllowedEnhancedGroups = KnowledgeImpactGroup.None;

    public const string EvaluatorVersion = "qp-hist-eval-v1";

    /// <summary>Primary graded markets (counting stats with historical coverage).</summary>
    public static readonly IReadOnlyList<PredictionMarketType> GradedMarkets =
    [
        PredictionMarketType.PassingYards,
        PredictionMarketType.RushingYards,
        PredictionMarketType.ReceivingYards,
        PredictionMarketType.Receptions
    ];

    public const int TopNSmall = 5;

    public const int TopNLarge = 10;

    public const string SelectionSummary =
        "Enhanced Quick Picks V1 uses AllowedEnhancedGroups=None because Knowledge Impact V1 " +
        "rejected Usage (holdout regression), RoleHealth (negative development), and RecentForm " +
        "(neutral). Matchup unavailable. Enhanced is observational/pass-through until a future " +
        "knowledge group earns enablement on this evaluation surface. 2024 was not used to choose this.";
}

/// <summary>
/// Deterministic historical Quick Pick at a cutoff.
/// Preserves Quick Picks semantics: player × counting market × projected value × ranking.
/// Does not invent sportsbook O/U lines (no historical prop-line archive).
/// </summary>
public sealed class QuickPickHistoricalPrediction
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Position Position { get; init; }

    public string? Team { get; init; }

    /// <summary>Always CountingStatProjection for V1 (no archived lines).</summary>
    public required string PredictionType { get; init; }

    public required PredictionMarketType Market { get; init; }

    public required double ProjectedValue { get; init; }

    /// <summary>1 = highest projected / ranking score within (season, week, market).</summary>
    public required int RankInMarket { get; init; }

    /// <summary>
    /// Ranking score used for market ordering. Baseline: projected value.
    /// Enhanced: may incorporate bounded knowledge OpportunityScore deltas when allowed.
    /// </summary>
    public required double RankingScore { get; init; }

    public int? Confidence { get; init; }

    public PredictionContext? KnowledgeContext { get; init; }

    public required bool KnowledgeAttached { get; init; }

    public required DateTimeOffset CutoffTimestamp { get; init; }

    public required QuickPickMode Mode { get; init; }

    public required string EvaluatorVersion { get; init; }
}

/// <summary>Outcome attached after prediction finalization.</summary>
public sealed class QuickPickGradedPrediction
{
    public required QuickPickHistoricalPrediction Prediction { get; init; }

    public required double ActualValue { get; init; }

    /// <summary>1 = highest actual within (season, week, market).</summary>
    public required int ActualRankInMarket { get; init; }

    public required double AbsoluteError { get; init; }

    public required double SignedError { get; init; }

    public required int RankAbsoluteError { get; init; }

    public required bool InProjectedTop5 { get; init; }

    public required bool InActualTop5 { get; init; }

    public required bool Top5Hit { get; init; }

    public required bool InProjectedTop10 { get; init; }

    public required bool InActualTop10 { get; init; }

    public required bool Top10Hit { get; init; }
}

public sealed class QuickPickSeasonScorecard
{
    public required int Season { get; init; }

    public required QuickPickMode Mode { get; init; }

    public required KnowledgeImpactGroup ActiveGroups { get; init; }

    public required int WeeksEvaluated { get; init; }

    public required int PredictionsEvaluated { get; init; }

    /// <summary>Mean |projected − actual| across graded counting-stat predictions.</summary>
    public required double MeanAbsoluteError { get; init; }

    /// <summary>Mean (projected − actual); positive = over-projection.</summary>
    public required double MeanBias { get; init; }

    /// <summary>Share of projected-top-5 picks that also finished actual-top-5 (among projected-top-5).</summary>
    public required double Top5HitRate { get; init; }

    public required double Top10HitRate { get; init; }

    /// <summary>Mean rank absolute error (lower is better).</summary>
    public required double MeanRankAbsoluteError { get; init; }

    public required double? AverageConfidence { get; init; }

    /// <summary>Sum of −AbsoluteError (higher / less negative = better projection value).</summary>
    public required double TotalPredictionValue { get; init; }

    public required IReadOnlyList<QuickPickGradedPrediction> Graded { get; init; }

    public required string EvaluatorVersion { get; init; }
}

public sealed class QuickPickChangeRecord
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Position Position { get; init; }

    public required PredictionMarketType Market { get; init; }

    public required double BaselineProjectedValue { get; init; }

    public required double EnhancedProjectedValue { get; init; }

    public required double BaselineRankingScore { get; init; }

    public required double EnhancedRankingScore { get; init; }

    public required int BaselineRank { get; init; }

    public required int EnhancedRank { get; init; }

    public required double Magnitude { get; init; }

    public required double BaselineAbsoluteError { get; init; }

    public required double EnhancedAbsoluteError { get; init; }

    public required int BaselineRankError { get; init; }

    public required int EnhancedRankError { get; init; }

    public required string LedgerClass { get; init; }

    public double? ActualValue { get; init; }

    public int? Confidence { get; init; }

    public int? KnowledgeConfidence { get; init; }

    public double? RecentFormValue { get; init; }

    public string? RecentFormStatement { get; init; }

    public string? RecentFormDirection { get; init; }
}

public sealed class QuickPickChangeAnalysis
{
    public required int PredictionsCompared { get; init; }

    public required int PredictionsChanged { get; init; }

    public required int PredictionsUnchanged { get; init; }

    /// <summary>Subset of changed predictions where RankInMarket differed.</summary>
    public required int RanksChanged { get; init; }

    public required double PercentChanged { get; init; }

    public required double PercentRanksChanged { get; init; }

    public required double AverageMagnitudeOfChange { get; init; }

    public required double BaselineMeanAbsoluteError { get; init; }

    public required double EnhancedMeanAbsoluteError { get; init; }

    public required double BaselineTop5HitRate { get; init; }

    public required double EnhancedTop5HitRate { get; init; }

    public required double BaselineTotalPredictionValue { get; init; }

    public required double EnhancedTotalPredictionValue { get; init; }

    public required bool PredictionsIdentical { get; init; }

    public required IReadOnlyList<QuickPickChangeRecord> Changed { get; init; }

    public required IReadOnlyList<QuickPickChangeRecord> Helped { get; init; }

    public required IReadOnlyList<QuickPickChangeRecord> Hurt { get; init; }

    public required IReadOnlyList<QuickPickChangeRecord> Neutral { get; init; }

    public required IReadOnlyList<QuickPickChangeRecord> BestChanges { get; init; }

    public required IReadOnlyList<QuickPickChangeRecord> WorstChanges { get; init; }
}

public sealed class QuickPicksHistoricalEvaluationReport
{
    public required string EvaluationId { get; init; }

    public required string EvaluatorVersion { get; init; }

    public required IReadOnlyList<int> DevelopmentSeasons { get; init; }

    public required int HoldoutSeason { get; init; }

    public required KnowledgeImpactGroup AllowedEnhancedGroups { get; init; }

    public required string SelectionSummary { get; init; }

    public required bool UsedHoldoutDuringDevelopment { get; init; }

    public required bool RejectedKnowledgeTransformsReenabled { get; init; }

    public required bool ProjectionV2Unchanged { get; init; }

    public required bool ConfidenceV2Unchanged { get; init; }

    public required bool DecisionPolicyV1Unchanged { get; init; }

    public required IReadOnlyList<QuickPickSeasonScorecard> DevelopmentBaseline { get; init; }

    public required IReadOnlyList<QuickPickSeasonScorecard> DevelopmentEnhanced { get; init; }

    public required QuickPickSeasonScorecard HoldoutBaseline { get; init; }

    public required QuickPickSeasonScorecard HoldoutEnhanced { get; init; }

    public required QuickPickChangeAnalysis DevelopmentChangeAnalysis { get; init; }

    public required QuickPickChangeAnalysis HoldoutChangeAnalysis { get; init; }

    public required string Verdict { get; init; }

    public string ToReportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("QUICK PICKS HISTORICAL EVALUATION V1");
        sb.AppendLine($"EvaluationId: {EvaluationId}");
        sb.AppendLine($"EvaluatorVersion: {EvaluatorVersion}");
        sb.AppendLine($"Development seasons: {string.Join(", ", DevelopmentSeasons)}");
        sb.AppendLine($"Holdout season: {HoldoutSeason}");
        sb.AppendLine($"AllowedEnhancedGroups: {AllowedEnhancedGroups}");
        sb.AppendLine($"UsedHoldoutDuringDevelopment: {UsedHoldoutDuringDevelopment}");
        sb.AppendLine($"RejectedTransformsReenabled: {RejectedKnowledgeTransformsReenabled}");
        sb.AppendLine($"ProjectionV2Unchanged: {ProjectionV2Unchanged}");
        sb.AppendLine($"ConfidenceV2Unchanged: {ConfidenceV2Unchanged}");
        sb.AppendLine($"DecisionPolicyV1Unchanged: {DecisionPolicyV1Unchanged}");
        sb.AppendLine();
        sb.AppendLine(SelectionSummary);
        sb.AppendLine();
        AppendScope(sb, "DEVELOPMENT BASELINE", DevelopmentBaseline);
        AppendScope(sb, "DEVELOPMENT ENHANCED", DevelopmentEnhanced);
        AppendChange(sb, "DEVELOPMENT CHANGE ANALYSIS", DevelopmentChangeAnalysis);
        sb.AppendLine();
        sb.AppendLine("=== OFFICIAL HOLDOUT 2024 ===");
        AppendCard(sb, "HOLDOUT BASELINE", HoldoutBaseline);
        AppendCard(sb, "HOLDOUT ENHANCED", HoldoutEnhanced);
        AppendChange(sb, "HOLDOUT CHANGE ANALYSIS", HoldoutChangeAnalysis);
        sb.AppendLine();
        sb.AppendLine($"VERDICT: {Verdict}");
        return sb.ToString();
    }

    private static void AppendScope(StringBuilder sb, string title, IReadOnlyList<QuickPickSeasonScorecard> cards)
    {
        sb.AppendLine($"=== {title} ===");
        foreach (var c in cards)
        {
            AppendCard(sb, $"{c.Season}", c);
        }

        if (cards.Count > 0)
        {
            var mae = cards.Average(c => c.MeanAbsoluteError);
            var top5 = cards.Average(c => c.Top5HitRate);
            var tot = cards.Sum(c => c.TotalPredictionValue);
            var n = cards.Sum(c => c.PredictionsEvaluated);
            sb.AppendLine(
                $"  AGGREGATE: n={n} meanMAE={mae:0.000} meanTop5={top5:0.0}% totalValue={tot:0.0}");
        }

        sb.AppendLine();
    }

    private static void AppendCard(StringBuilder sb, string title, QuickPickSeasonScorecard c)
    {
        sb.AppendLine(
            $"  {title}: weeks={c.WeeksEvaluated} preds={c.PredictionsEvaluated} " +
            $"MAE={c.MeanAbsoluteError:0.000} bias={c.MeanBias:0.000} " +
            $"top5={c.Top5HitRate:0.0}% top10={c.Top10HitRate:0.0}% " +
            $"rankMAE={c.MeanRankAbsoluteError:0.000} totVal={c.TotalPredictionValue:0.0} " +
            $"avgConf={c.AverageConfidence?.ToString("0.0") ?? "n/a"} mode={c.Mode} groups={c.ActiveGroups}");
    }

    private static void AppendChange(StringBuilder sb, string title, QuickPickChangeAnalysis a)
    {
        sb.AppendLine($"=== {title} ===");
        sb.AppendLine(
            $"  compared={a.PredictionsCompared} changed={a.PredictionsChanged} " +
            $"unchanged={a.PredictionsUnchanged} pctChanged={a.PercentChanged:0.00}% " +
            $"ranksChanged={a.RanksChanged} ({a.PercentRanksChanged:0.00}%) " +
            $"avgMagnitude={a.AverageMagnitudeOfChange:0.000} identical={a.PredictionsIdentical}");
        sb.AppendLine(
            $"  MAE baseline→enhanced: {a.BaselineMeanAbsoluteError:0.000} → {a.EnhancedMeanAbsoluteError:0.000}");
        sb.AppendLine(
            $"  Top5 baseline→enhanced: {a.BaselineTop5HitRate:0.0}% → {a.EnhancedTop5HitRate:0.0}%");
        sb.AppendLine(
            $"  Value baseline→enhanced: {a.BaselineTotalPredictionValue:0.0} → {a.EnhancedTotalPredictionValue:0.0}");
        sb.AppendLine($"  ledger HELPED={a.Helped.Count} HURT={a.Hurt.Count} NEUTRAL={a.Neutral.Count}");
        foreach (var c in a.BestChanges.Take(5))
        {
            sb.AppendLine(
                $"  BEST {c.LedgerClass}: {c.Season} W{c.Week} {c.PlayerName} {c.Market} " +
                $"rank {c.BaselineRank}→{c.EnhancedRank} (err {c.BaselineRankError}→{c.EnhancedRankError}) " +
                $"form={c.RecentFormValue?.ToString("0") ?? "n/a"} actual={c.ActualValue:0.#}");
        }

        foreach (var c in a.WorstChanges.Take(5))
        {
            sb.AppendLine(
                $"  WORST {c.LedgerClass}: {c.Season} W{c.Week} {c.PlayerName} {c.Market} " +
                $"rank {c.BaselineRank}→{c.EnhancedRank} (err {c.BaselineRankError}→{c.EnhancedRankError}) " +
                $"form={c.RecentFormValue?.ToString("0") ?? "n/a"} actual={c.ActualValue:0.#}");
        }

        sb.AppendLine();
    }
}

/// <summary>
/// Grading formulas for historical Quick Picks V1.
/// Primary: counting-stat projection MAE / bias / total prediction value (−MAE sum).
/// Secondary: Top-N ranking hit rate and mean rank absolute error within market.
/// Explicitly does NOT grade sportsbook O/U hit rate (no line archive).
/// </summary>
public static class QuickPickHistoricalGrading
{
    public const string PredictionTypeLabel = "CountingStatProjection";

    public static double AbsoluteError(double projected, double actual) =>
        Math.Abs(projected - actual);

    public static double SignedError(double projected, double actual) =>
        projected - actual;

    public static double TotalPredictionValue(IEnumerable<double> absoluteErrors) =>
        -absoluteErrors.Sum();

    public static bool TopNHit(bool inProjectedTopN, bool inActualTopN) =>
        inProjectedTopN && inActualTopN;

    public static string ClassifyLedger(int baselineRankError, int enhancedRankError, double magnitude)
    {
        if (magnitude <= 1e-9)
        {
            return "NEUTRAL";
        }

        if (enhancedRankError < baselineRankError)
        {
            return "HELPED";
        }

        if (enhancedRankError > baselineRankError)
        {
            return "HURT";
        }

        return "NEUTRAL";
    }
}
