using Playbook.Core.Decisions;
using Playbook.Core.Leagues;
using Playbook.Core.Players;

namespace Playbook.Core.Replay;

/// <summary>Request to replay an inclusive week range for one season.</summary>
public sealed class MultiWeekReplayRequest
{
    public required int Season { get; init; }

    public required int StartWeek { get; init; }

    public required int EndWeek { get; init; }

    public ScoringType ScoringType { get; init; } = ScoringType.Ppr;

    public string? FixtureId { get; init; }

    public DecisionKind DecisionKind { get; init; } = DecisionKind.StartSit;

    /// <summary>
    /// When true, weeks that fail (e.g. insufficient lab roster) are recorded as skips
    /// instead of aborting the season run.
    /// </summary>
    public bool ContinueOnWeekFailure { get; init; } = true;
}

/// <summary>Player-level projection evaluation (independent of Start/Sit decisions).</summary>
public sealed class PlayerProjectionEvaluation
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Position Position { get; init; }

    public required double PredictedPoints { get; init; }

    public required double ActualPoints { get; init; }

    public required double AbsoluteError { get; init; }

    public required double SignedError { get; init; }

    public required double SquaredError { get; init; }

    public double? BaselineRecentAveragePoints { get; init; }

    public double? BaselineOpportunityAwarePoints { get; init; }

    public double? BaselineRecentAbsoluteError { get; init; }

    public double? BaselineOpportunityAbsoluteError { get; init; }

    public DataSufficiency? DataSufficiency { get; init; }

    public int? ProjectionConfidence { get; init; }

    public IReadOnlyList<int> SourceWeeks { get; init; } = [];

    public int? OpportunityScore { get; init; }

    public int? UsageScore { get; init; }

    public int? RecentProductionScore { get; init; }
}

/// <summary>One skipped week during a multi-week run.</summary>
public sealed class WeekReplaySkip
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required string Reason { get; init; }
}

/// <summary>Confidence calibration bucket (measurement only — does not alter confidence).</summary>
public sealed class ConfidenceBucketStats
{
    public required string Label { get; init; }

    public required int MinInclusive { get; init; }

    public required int MaxExclusive { get; init; }

    public required int DecisionCount { get; init; }

    public required int GradedCount { get; init; }

    public required int CorrectCount { get; init; }

    public required double? ActualSuccessRatePercent { get; init; }

    public required double? AverageDecisionValue { get; init; }

    public required double? AverageConfidence { get; init; }
}

/// <summary>Structured record of a meaningful incorrect historical decision.</summary>
public sealed class FailureLedgerEntry
{
    public required Guid DecisionId { get; init; }

    public required int Season { get; init; }

    public required int Week { get; init; }

    public required DateTimeOffset InformationCutoff { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Position Position { get; init; }

    public required DecisionRecommendation Recommendation { get; init; }

    public required double PredictedPoints { get; init; }

    public required double? ActualPoints { get; init; }

    public required int Confidence { get; init; }

    public required DataSufficiency? DataSufficiency { get; init; }

    public string? AlternativePlayerName { get; init; }

    public double? AlternativePredictedPoints { get; init; }

    public double? AlternativeActualPoints { get; init; }

    public double? DecisionCost { get; init; }

    public required string EvaluationSummary { get; init; }

    public required IReadOnlyList<string> SupportingEvidence { get; init; }

    public required IReadOnlyList<string> OpposingEvidence { get; init; }

    public required IReadOnlyList<string> Unknowns { get; init; }

    public required IReadOnlyList<string> Rationale { get; init; }

    public IReadOnlyList<int> ProjectionSourceWeeks { get; init; } = [];
}

/// <summary>Observable failure / performance pattern (no invented explanations).</summary>
public sealed class ObservablePattern
{
    public required string Dimension { get; init; }

    public required string Bucket { get; init; }

    public required int SampleSize { get; init; }

    public double? MetricValue { get; init; }

    public required string MetricName { get; init; }

    public required string Notes { get; init; }
}

/// <summary>Per-week rollup inside a season scorecard.</summary>
public sealed class WeekScorecard
{
    public required int Week { get; init; }

    public required DateTimeOffset? InformationCutoff { get; init; }

    public required bool Completed { get; init; }

    public string? SkipReason { get; init; }

    public required int DecisionCount { get; init; }

    public required int CorrectCount { get; init; }

    public required int IncorrectCount { get; init; }

    public required double? DecisionAccuracyPercent { get; init; }

    public required double? AverageDecisionValue { get; init; }

    public required double? ProjectionMae { get; init; }

    public required double? BaselineAMae { get; init; }

    public required double? BaselineBMae { get; init; }

    public required double AverageConfidence { get; init; }

    public required int PlayersWithValidProjection { get; init; }

    public required int PlayersEvaluated { get; init; }
}

/// <summary>Position rollup.</summary>
public sealed class PositionScorecard
{
    public required Position Position { get; init; }

    public required int ProjectionCount { get; init; }

    public required double? ProjectionMae { get; init; }

    public required double? BaselineAMae { get; init; }

    public required double? BaselineBMae { get; init; }

    public required double? SignedBias { get; init; }

    public required int DecisionCount { get; init; }

    public required int GradedDecisionCount { get; init; }

    public required double? DecisionAccuracyPercent { get; init; }

    public required double? AverageDecisionValue { get; init; }
}

/// <summary>Historical data coverage summary for a multi-week run.</summary>
public sealed class HistoricalDataQualityReport
{
    public required int WeeksRequested { get; init; }

    public required int WeeksCompleted { get; init; }

    public required int WeeksSkipped { get; init; }

    public required int PlayersEvaluated { get; init; }

    public required int PlayersWithValidProjection { get; init; }

    public required double PercentPlayersWithValidProjection { get; init; }

    public required int DecisionsGenerated { get; init; }

    public required int DecisionsGraded { get; init; }

    public required int DecisionsSkippedInsufficientData { get; init; }

    public required int ProjectionEvaluations { get; init; }

    public required double PercentWithInjurySignal { get; init; }

    public required double PercentWithUsageSignal { get; init; }

    public required double PercentWithRoleSignal { get; init; }

    public required double PercentSufficientHistory { get; init; }

    public required double PercentLimitedHistory { get; init; }

    public required double PercentInsufficientHistory { get; init; }

    public required IReadOnlyList<string> UnavailableInformation { get; init; }

    public required IReadOnlyList<WeekReplaySkip> SkippedWeeks { get; init; }
}

/// <summary>Full-season (or multi-week) measurement scorecard.</summary>
public sealed class SeasonScorecard
{
    public required int Season { get; init; }

    public required int StartWeek { get; init; }

    public required int EndWeek { get; init; }

    public required ScoringType ScoringType { get; init; }

    public required string? FixtureId { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required IReadOnlyList<HistoricalReplayReport> WeekReports { get; init; }

    public required IReadOnlyList<WeekReplaySkip> SkippedWeeks { get; init; }

    public required IReadOnlyList<WeekScorecard> Weeks { get; init; }

    public required IReadOnlyList<PlayerProjectionEvaluation> ProjectionEvaluations { get; init; }

    public required IReadOnlyList<ReplayDecisionGrade> AllGrades { get; init; }

    public required IReadOnlyList<DecisionRecord> AllDecisionRecords { get; init; }

    public required IReadOnlyList<FailureLedgerEntry> FailureLedger { get; init; }

    public required IReadOnlyList<ConfidenceBucketStats> ConfidenceBuckets { get; init; }

    public required IReadOnlyList<PositionScorecard> ByPosition { get; init; }

    public required IReadOnlyList<ObservablePattern> ObservablePatterns { get; init; }

    public required HistoricalDataQualityReport DataQuality { get; init; }

    // Projection aggregates (fair common set for model vs baselines)
    public required int FairProjectionCount { get; init; }

    public required double? CurrentModelMae { get; init; }

    public required double? CurrentModelRmse { get; init; }

    public required double? CurrentModelSignedBias { get; init; }

    public required double? BaselineAMae { get; init; }

    public required double? BaselineBMae { get; init; }

    public required string? BetterProjectionBaseline { get; init; }

    // Decision aggregates
    public required int TotalDecisions { get; init; }

    public required int CorrectDecisions { get; init; }

    public required int IncorrectDecisions { get; init; }

    public required int UngradedDecisions { get; init; }

    public required double? DecisionAccuracyPercent { get; init; }

    public required double? AverageDecisionValue { get; init; }

    public required double? MedianDecisionValue { get; init; }

    public required double? TotalDecisionValue { get; init; }

    public required double AverageConfidence { get; init; }

    public string ToScorecardText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"SEASON SCORECARD — {Season} Weeks {StartWeek}-{EndWeek}");
        sb.AppendLine($"Scoring: {ScoringType}");
        sb.AppendLine($"Generated: {GeneratedAt:u}");
        sb.AppendLine();
        sb.AppendLine("COVERAGE");
        sb.AppendLine($"  Weeks completed: {DataQuality.WeeksCompleted}/{DataQuality.WeeksRequested} (skipped {DataQuality.WeeksSkipped})");
        sb.AppendLine($"  Players evaluated: {DataQuality.PlayersEvaluated}");
        sb.AppendLine($"  Valid projections: {DataQuality.PlayersWithValidProjection} ({DataQuality.PercentPlayersWithValidProjection:0.#}%)");
        sb.AppendLine($"  Decisions: {TotalDecisions} (graded {CorrectDecisions + IncorrectDecisions})");
        sb.AppendLine($"  Injury signal coverage: {DataQuality.PercentWithInjurySignal:0.#}%");
        sb.AppendLine($"  Usage signal coverage: {DataQuality.PercentWithUsageSignal:0.#}%");
        sb.AppendLine($"  Role signal coverage: {DataQuality.PercentWithRoleSignal:0.#}%");
        sb.AppendLine();
        sb.AppendLine("PROJECTION (fair common set)");
        sb.AppendLine($"  N: {FairProjectionCount}");
        sb.AppendLine($"  Current model MAE: {Fmt(CurrentModelMae)}");
        sb.AppendLine($"  Current model RMSE: {Fmt(CurrentModelRmse)}");
        sb.AppendLine($"  Current model bias (actual-pred): {Fmt(CurrentModelSignedBias)}");
        sb.AppendLine($"  Baseline A MAE (recent avg): {Fmt(BaselineAMae)}");
        sb.AppendLine($"  Baseline B MAE (opportunity-aware): {Fmt(BaselineBMae)}");
        sb.AppendLine($"  Better: {BetterProjectionBaseline ?? "n/a"}");
        sb.AppendLine();
        sb.AppendLine("DECISION");
        sb.AppendLine($"  Accuracy: {FmtPct(DecisionAccuracyPercent)} ({CorrectDecisions}/{CorrectDecisions + IncorrectDecisions})");
        sb.AppendLine($"  Avg decision value: {Fmt(AverageDecisionValue)}");
        sb.AppendLine($"  Median decision value: {Fmt(MedianDecisionValue)}");
        sb.AppendLine($"  Total decision value: {Fmt(TotalDecisionValue)}");
        sb.AppendLine($"  Avg confidence: {AverageConfidence:0.#}");
        sb.AppendLine();
        sb.AppendLine("CONFIDENCE CALIBRATION (measurement only)");
        foreach (var b in ConfidenceBuckets)
        {
            sb.AppendLine(
                $"  {b.Label}: n={b.DecisionCount} graded={b.GradedCount} " +
                $"success={FmtPct(b.ActualSuccessRatePercent)} avgVal={Fmt(b.AverageDecisionValue)}");
        }

        sb.AppendLine();
        sb.AppendLine("POSITION");
        foreach (var p in ByPosition)
        {
            sb.AppendLine(
                $"  {p.Position}: projN={p.ProjectionCount} mae={Fmt(p.ProjectionMae)} " +
                $"decN={p.DecisionCount} acc={FmtPct(p.DecisionAccuracyPercent)} avgVal={Fmt(p.AverageDecisionValue)}");
        }

        sb.AppendLine();
        sb.AppendLine("WEEK-BY-WEEK");
        foreach (var w in Weeks)
        {
            if (!w.Completed)
            {
                sb.AppendLine($"  W{w.Week}: SKIPPED — {w.SkipReason}");
                continue;
            }

            sb.AppendLine(
                $"  W{w.Week}: mae={Fmt(w.ProjectionMae)} acc={FmtPct(w.DecisionAccuracyPercent)} " +
                $"val={Fmt(w.AverageDecisionValue)} dec={w.DecisionCount} conf={w.AverageConfidence:0.#}");
        }

        sb.AppendLine();
        sb.AppendLine("OBSERVABLE PATTERNS");
        if (ObservablePatterns.Count == 0)
        {
            sb.AppendLine("  (none with sufficient sample)");
        }
        else
        {
            foreach (var pattern in ObservablePatterns)
            {
                sb.AppendLine(
                    $"  [{pattern.Dimension}/{pattern.Bucket}] {pattern.MetricName}={Fmt(pattern.MetricValue)} " +
                    $"n={pattern.SampleSize} — {pattern.Notes}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"FAILURE LEDGER: {FailureLedger.Count} incorrect decisions (see structured records)");
        var top = FailureLedger
            .OrderBy(f => f.DecisionCost ?? 0)
            .Take(10)
            .ToList();
        foreach (var f in top)
        {
            sb.AppendLine(
                $"  W{f.Week} {f.PlayerName} ({f.Position}) {f.Recommendation} conf={f.Confidence} " +
                $"cost={Fmt(f.DecisionCost)} — {f.EvaluationSummary}");
        }

        sb.AppendLine();
        sb.AppendLine("UNAVAILABLE INFORMATION");
        foreach (var u in DataQuality.UnavailableInformation.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"  - {u}");
        }

        return sb.ToString();

        static string Fmt(double? v) => v is null ? "n/a" : v.Value.ToString("0.00");
        static string FmtPct(double? v) => v is null ? "n/a" : $"{v.Value:0.#}%";
    }
}
