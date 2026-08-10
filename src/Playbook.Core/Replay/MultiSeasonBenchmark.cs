using Playbook.Core.Decisions;
using Playbook.Core.Leagues;
using Playbook.Core.Players;

namespace Playbook.Core.Replay;

/// <summary>
/// Out-of-sample evaluation roles. Architecture only — no training occurs here.
/// Prevents future tuning from silently contaminating held-out seasons.
/// </summary>
public enum EvaluationSeasonRole
{
    /// <summary>Allowed for measurement and future controlled development.</summary>
    Development = 0,

    /// <summary>Frozen holdout. Do not tune against these seasons.</summary>
    HoldoutTest = 1,

    /// <summary>Frozen historical benchmark (e.g. 2018). Compare only; do not optimize to it.</summary>
    FrozenBenchmark = 2
}

/// <summary>Classification of a measured weakness across seasons.</summary>
public enum StructuralFindingKind
{
    ConfirmedStructuralProblem = 0,
    PossibleProblem = 1,
    SeasonSpecificAnomaly = 2,
    DataLimitation = 3
}

/// <summary>Request to benchmark multiple complete seasons with a frozen model.</summary>
public sealed class MultiSeasonBenchmarkRequest
{
    public required IReadOnlyList<int> Seasons { get; init; }

    public ScoringType ScoringType { get; init; } = ScoringType.Ppr;

    public string? FixtureId { get; init; } = "nflverse";

    public DecisionKind DecisionKind { get; init; } = DecisionKind.StartSit;

    public bool ContinueOnWeekFailure { get; init; } = true;

    /// <summary>
    /// Optional explicit week bounds. When null, each season uses weeks 1..max REG week.
    /// </summary>
    public int? StartWeek { get; init; }

    public int? EndWeek { get; init; }

    /// <summary>
    /// Optional role map for OOS discipline. Unmapped seasons default to Development.
    /// </summary>
    public IReadOnlyDictionary<int, EvaluationSeasonRole> SeasonRoles { get; init; } =
        new Dictionary<int, EvaluationSeasonRole>();
}

/// <summary>Default diverse-era sample used by developer helpers (not hardcoded in the runner).</summary>
public static class DefaultMultiSeasonBenchmarkSample
{
    /// <summary>
    /// Diverse eras: pre-17-game / mid-PPR boom (2015), established modern (2018),
    /// 18-game / COVID-era aftermath (2021), recent environment (2024).
    /// </summary>
    public static readonly IReadOnlyList<int> Seasons = [2015, 2018, 2021, 2024];

    public static readonly IReadOnlyDictionary<int, EvaluationSeasonRole> DefaultRoles =
        new Dictionary<int, EvaluationSeasonRole>
        {
            [2015] = EvaluationSeasonRole.Development,
            [2018] = EvaluationSeasonRole.FrozenBenchmark,
            [2021] = EvaluationSeasonRole.Development,
            [2024] = EvaluationSeasonRole.HoldoutTest
        };
}

/// <summary>Frozen 2018 season metrics — regression lock. Do not optimize the model to keep these green.</summary>
public static class Frozen2018SeasonBenchmark
{
    public const int Season = 2018;
    public const int StartWeek = 1;
    public const int EndWeek = 17;
    public const int FairProjectionCount = 261;
    public const double CurrentModelMae = 11.62;
    public const double BaselineAMae = 9.03;
    public const double BaselineBMae = 11.62;
    public const double CurrentModelSignedBias = -8.53;
    public const int TotalDecisions = 99;
    public const int CorrectDecisions = 57;
    public const int IncorrectDecisions = 37;
    public const double DecisionAccuracyPercent = 60.6;
    public const double TotalDecisionValue = 300.0;
    public const double AverageConfidence = 28.4;
    public const double AverageDecisionValue = 3.19;
    public const double MedianDecisionValue = 3.25;
}

/// <summary>Per-season summary row inside the cross-season scorecard.</summary>
public sealed class SeasonBenchmarkSummary
{
    public required int Season { get; init; }

    public required EvaluationSeasonRole Role { get; init; }

    public required int WeeksCompleted { get; init; }

    public required int WeeksRequested { get; init; }

    public required int FairProjectionCount { get; init; }

    public required double? CurrentModelMae { get; init; }

    public required double? BaselineAMae { get; init; }

    public required double? BaselineBMae { get; init; }

    public required double? Bias { get; init; }

    public required double? MaeDeltaVsBaselineA { get; init; }

    public required double? MaePctChangeVsBaselineA { get; init; }

    public required bool? CurrentBeatsBaselineA { get; init; }

    public required int TotalDecisions { get; init; }

    public required int GradedDecisions { get; init; }

    public required double? DecisionAccuracyPercent { get; init; }

    public required double? AverageDecisionValue { get; init; }

    public required double? MedianDecisionValue { get; init; }

    public required double? TotalDecisionValue { get; init; }

    public required double? WorstDecisionCost { get; init; }

    public required double? BestDecisionValue { get; init; }

    public required double AverageConfidence { get; init; }

    public required int PlayersEvaluated { get; init; }

    public required int PlayersWithValidProjection { get; init; }

    public required double PercentPlayersWithValidProjection { get; init; }

    public required SeasonScorecard Scorecard { get; init; }
}

/// <summary>Baseline A head-to-head comparison for one season or aggregate.</summary>
public sealed class BaselineComparisonRow
{
    public required string Scope { get; init; }

    public required int FairProjectionCount { get; init; }

    public required double? CurrentModelMae { get; init; }

    public required double? BaselineAMae { get; init; }

    public required double? AbsoluteDifference { get; init; }

    public required double? PercentChangeVsBaselineA { get; init; }

    public required string Winner { get; init; }
}

/// <summary>Bias slice used for over-projection diagnosis.</summary>
public sealed class BiasBreakdownRow
{
    public required string Dimension { get; init; }

    public required string Bucket { get; init; }

    public required int SampleSize { get; init; }

    public required double? MeanSignedBias { get; init; }

    public required double? Mae { get; init; }

    public required string Notes { get; init; }
}

/// <summary>Decision-quality slice across seasons.</summary>
public sealed class DecisionBreakdownRow
{
    public required string Dimension { get; init; }

    public required string Bucket { get; init; }

    public required int DecisionCount { get; init; }

    public required int GradedCount { get; init; }

    public required double? AccuracyPercent { get; init; }

    public required double? AverageDecisionValue { get; init; }

    public required double? MedianDecisionValue { get; init; }

    public required double? TotalDecisionValue { get; init; }

    public required double? WorstDecisionCost { get; init; }

    public required double? BestDecisionValue { get; init; }
}

/// <summary>Classified structural finding from the multi-season benchmark.</summary>
public sealed class StructuralFinding
{
    public required StructuralFindingKind Kind { get; init; }

    public required string Title { get; init; }

    public required string Evidence { get; init; }

    public required IReadOnlyList<int> SeasonsObserved { get; init; }
}

/// <summary>Full multi-season benchmark report (frozen model measurement).</summary>
public sealed class MultiSeasonBenchmarkReport
{
    public required IReadOnlyList<int> Seasons { get; init; }

    public required ScoringType ScoringType { get; init; }

    public required string? FixtureId { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required string ModelFreezeNote { get; init; }

    public required IReadOnlyList<SeasonBenchmarkSummary> SeasonSummaries { get; init; }

    public required IReadOnlyList<SeasonScorecard> SeasonScorecards { get; init; }

    public required IReadOnlyList<BaselineComparisonRow> BaselineComparisons { get; init; }

    public required int SeasonsCurrentWins { get; init; }

    public required int SeasonsBaselineAWins { get; init; }

    public required int SeasonsTied { get; init; }

    public required IReadOnlyList<BiasBreakdownRow> BiasBreakdown { get; init; }

    public required IReadOnlyList<DecisionBreakdownRow> DecisionBreakdown { get; init; }

    public required IReadOnlyList<ConfidenceBucketStats> ConfidenceBuckets { get; init; }

    public required IReadOnlyList<FailureLedgerEntry> CrossSeasonFailureLedger { get; init; }

    public required IReadOnlyList<PlayerProjectionEvaluation> LargestProjectionErrors { get; init; }

    public required IReadOnlyList<StructuralFinding> StructuralFindings { get; init; }

    public required IReadOnlyDictionary<int, EvaluationSeasonRole> SeasonRoles { get; init; }

    // Aggregate totals
    public required int TotalWeeksCompleted { get; init; }

    public required int TotalFairProjectionEvaluations { get; init; }

    public required int TotalDecisions { get; init; }

    public required int TotalGradedDecisions { get; init; }

    public required double? AggregateCurrentModelMae { get; init; }

    public required double? AggregateBaselineAMae { get; init; }

    public required double? AggregateBaselineBMae { get; init; }

    public required double? AggregateBias { get; init; }

    public required double? AggregateDecisionAccuracyPercent { get; init; }

    public required double? AggregateAverageDecisionValue { get; init; }

    public required double? AggregateMedianDecisionValue { get; init; }

    public required double? AggregateTotalDecisionValue { get; init; }

    public required double? AggregateWorstDecisionCost { get; init; }

    public required double? AggregateBestDecisionValue { get; init; }

    public required double AggregateAverageConfidence { get; init; }

    public string ToReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("MULTI-SEASON HISTORICAL BENCHMARK");
        sb.AppendLine($"Seasons: {string.Join(", ", Seasons)}");
        sb.AppendLine($"Scoring: {ScoringType}");
        sb.AppendLine($"Generated: {GeneratedAt:u}");
        sb.AppendLine(ModelFreezeNote);
        sb.AppendLine();
        sb.AppendLine("OUT-OF-SAMPLE ROLES");
        foreach (var season in Seasons)
        {
            SeasonRoles.TryGetValue(season, out var role);
            if (role == 0 && !SeasonRoles.ContainsKey(season))
            {
                role = EvaluationSeasonRole.Development;
            }

            sb.AppendLine($"  {season}: {role}");
        }

        sb.AppendLine();
        sb.AppendLine("AGGREGATE");
        sb.AppendLine($"  Weeks: {TotalWeeksCompleted}");
        sb.AppendLine($"  Fair projections: {TotalFairProjectionEvaluations}");
        sb.AppendLine($"  Decisions: {TotalDecisions} (graded {TotalGradedDecisions})");
        sb.AppendLine($"  Current MAE: {Fmt(AggregateCurrentModelMae)}");
        sb.AppendLine($"  Baseline A MAE: {Fmt(AggregateBaselineAMae)}");
        sb.AppendLine($"  Baseline B MAE: {Fmt(AggregateBaselineBMae)}");
        sb.AppendLine($"  Bias (actual-pred): {Fmt(AggregateBias)}");
        sb.AppendLine($"  Decision accuracy: {FmtPct(AggregateDecisionAccuracyPercent)}");
        sb.AppendLine($"  Avg / median / total decision value: {Fmt(AggregateAverageDecisionValue)} / {Fmt(AggregateMedianDecisionValue)} / {Fmt(AggregateTotalDecisionValue)}");
        sb.AppendLine($"  Worst / best decision value: {Fmt(AggregateWorstDecisionCost)} / {Fmt(AggregateBestDecisionValue)}");
        sb.AppendLine($"  Avg confidence: {AggregateAverageConfidence:0.#}");
        sb.AppendLine($"  Seasons current wins vs A: {SeasonsCurrentWins}; baseline A wins: {SeasonsBaselineAWins}; ties: {SeasonsTied}");
        sb.AppendLine();
        sb.AppendLine("PER-SEASON");
        foreach (var s in SeasonSummaries)
        {
            sb.AppendLine(
                $"  {s.Season} [{s.Role}]: weeks={s.WeeksCompleted}/{s.WeeksRequested} " +
                $"projN={s.FairProjectionCount} mae={Fmt(s.CurrentModelMae)} baseA={Fmt(s.BaselineAMae)} " +
                $"Δ={Fmt(s.MaeDeltaVsBaselineA)} ({FmtPct(s.MaePctChangeVsBaselineA)}) " +
                $"bias={Fmt(s.Bias)} acc={FmtPct(s.DecisionAccuracyPercent)} " +
                $"valTot={Fmt(s.TotalDecisionValue)} conf={s.AverageConfidence:0.#}");
        }

        sb.AppendLine();
        sb.AppendLine("BASELINE A HEAD-TO-HEAD");
        foreach (var row in BaselineComparisons)
        {
            sb.AppendLine(
                $"  {row.Scope}: current={Fmt(row.CurrentModelMae)} A={Fmt(row.BaselineAMae)} " +
                $"diff={Fmt(row.AbsoluteDifference)} pct={FmtPct(row.PercentChangeVsBaselineA)} winner={row.Winner}");
        }

        sb.AppendLine();
        sb.AppendLine("BIAS BREAKDOWN");
        foreach (var row in BiasBreakdown.Take(40))
        {
            sb.AppendLine(
                $"  [{row.Dimension}/{row.Bucket}] n={row.SampleSize} bias={Fmt(row.MeanSignedBias)} mae={Fmt(row.Mae)} — {row.Notes}");
        }

        sb.AppendLine();
        sb.AppendLine("DECISION BREAKDOWN");
        foreach (var row in DecisionBreakdown.Take(40))
        {
            sb.AppendLine(
                $"  [{row.Dimension}/{row.Bucket}] n={row.DecisionCount} acc={FmtPct(row.AccuracyPercent)} " +
                $"avgVal={Fmt(row.AverageDecisionValue)} tot={Fmt(row.TotalDecisionValue)} " +
                $"worst={Fmt(row.WorstDecisionCost)} best={Fmt(row.BestDecisionValue)}");
        }

        sb.AppendLine();
        sb.AppendLine("CONFIDENCE CALIBRATION (all seasons, measurement only)");
        foreach (var b in ConfidenceBuckets)
        {
            sb.AppendLine(
                $"  {b.Label}: n={b.DecisionCount} graded={b.GradedCount} " +
                $"success={FmtPct(b.ActualSuccessRatePercent)} avgVal={Fmt(b.AverageDecisionValue)}");
        }

        sb.AppendLine();
        sb.AppendLine("STRUCTURAL FINDINGS");
        foreach (var f in StructuralFindings)
        {
            sb.AppendLine($"  [{f.Kind}] {f.Title}");
            sb.AppendLine($"    seasons=[{string.Join(",", f.SeasonsObserved)}]");
            sb.AppendLine($"    {f.Evidence}");
        }

        sb.AppendLine();
        sb.AppendLine($"CROSS-SEASON FAILURE LEDGER: {CrossSeasonFailureLedger.Count} incorrect decisions");
        foreach (var f in CrossSeasonFailureLedger.Take(15))
        {
            sb.AppendLine(
                $"  {f.Season} W{f.Week} {f.PlayerName} ({f.Position}) {f.Recommendation} " +
                $"conf={f.Confidence} cost={Fmt(f.DecisionCost)} — {f.EvaluationSummary}");
        }

        sb.AppendLine();
        sb.AppendLine("LARGEST PROJECTION ERRORS");
        foreach (var p in LargestProjectionErrors.Take(10))
        {
            sb.AppendLine(
                $"  {p.Season} W{p.Week} {p.PlayerName} ({p.Position}) pred={p.PredictedPoints:0.0} " +
                $"act={p.ActualPoints:0.0} abs={p.AbsoluteError:0.0} signed={p.SignedError:0.0}");
        }

        return sb.ToString();

        static string Fmt(double? v) => v is null ? "n/a" : v.Value.ToString("0.00");
        static string FmtPct(double? v) => v is null ? "n/a" : $"{v.Value:0.#}%";
    }
}
