using Playbook.Core.Decisions;
using Playbook.Core.Leagues;
using Playbook.Core.Players;

namespace Playbook.Core.Replay;

/// <summary>
/// Immutable grade for one historical decision after outcomes are revealed.
/// Comparative Start/Sit grades prefer alternative differentials over absolute score thresholds.
/// </summary>
public sealed class ReplayDecisionGrade
{
    public required Guid DecisionId { get; init; }

    public required int Season { get; init; }

    public required int Week { get; init; }

    public required DateTimeOffset InformationCutoff { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required Position Position { get; init; }

    public required DecisionRecommendation Recommendation { get; init; }

    public required int Confidence { get; init; }

    /// <summary>Experiment 2 calibrated confidence (informational; recommendations unchanged).</summary>
    public int? CalibratedConfidence { get; init; }

    public required double ExpectedValue { get; init; }

    public required double? ActualFantasyPoints { get; init; }

    public required double? ProjectionAbsoluteError { get; init; }

    public required double? ProjectionSignedError { get; init; }

    public double? ProjectionSquaredError { get; init; }

    public DataSufficiency? DataSufficiency { get; init; }

    public IReadOnlyList<int> ProjectionSourceWeeks { get; init; } = [];

    public double? BaselineRecentAveragePoints { get; init; }

    public double? BaselineOpportunityAwarePoints { get; init; }

    public double? BaselineRecentAbsoluteError { get; init; }

    public double? BaselineOpportunityAbsoluteError { get; init; }

    public int? OpportunityScore { get; init; }

    public int? UsageScore { get; init; }

    public int? RecentProductionScore { get; init; }

    /// <summary>|Expected − alternative expected| when an alternative exists.</summary>
    public double? RecommendationMargin { get; init; }

    public Guid? AlternativePlayerId { get; init; }

    public string? AlternativePlayerName { get; init; }

    public double? AlternativeExpectedValue { get; init; }

    public double? AlternativeActualFantasyPoints { get; init; }

    /// <summary>
    /// Actual points of recommended player minus alternative (START grading).
    /// Positive means the recommendation won on actuals.
    /// </summary>
    public double? ActualDecisionDifferential { get; init; }

    public required bool? WasCorrect { get; init; }

    public required bool MarginMattered { get; init; }

    public required string EvaluationSummary { get; init; }

    public required IReadOnlyList<string> SupportingEvidence { get; init; }

    public required IReadOnlyList<string> OpposingEvidence { get; init; }

    public required IReadOnlyList<string> Unknowns { get; init; }

    public required IReadOnlyList<string> Rationale { get; init; }
}

/// <summary>Aggregate report for one historical replay run.</summary>
public sealed class HistoricalReplayReport
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required DateTimeOffset InformationCutoff { get; init; }

    public required string LeagueName { get; init; }

    public required ScoringType ScoringType { get; init; }

    public required int DecisionCount { get; init; }

    public required int CorrectCount { get; init; }

    public required int IncorrectCount { get; init; }

    public required int UngradedCount { get; init; }

    public required double? DecisionAccuracyPercent { get; init; }

    public required double? AverageProjectionAbsoluteError { get; init; }

    public double? AverageProjectionSquaredError { get; init; }

    public double? BaselineRecentAverageMae { get; init; }

    public double? BaselineOpportunityAwareMae { get; init; }

    /// <summary>Which baseline had lower MAE on this replay (or tie).</summary>
    public string? BetterBaselineLabel { get; init; }

    public required double? AverageDecisionDifferential { get; init; }

    public required double AverageConfidence { get; init; }

    public required IReadOnlyList<ReplayDecisionGrade> Grades { get; init; }

    public required IReadOnlyList<DecisionRecord> DecisionRecords { get; init; }

    /// <summary>All roster players with a valid pre-week projection and revealed actual.</summary>
    public IReadOnlyList<PlayerProjectionEvaluation> ProjectionEvaluations { get; init; } = [];

    public int PlayersEvaluated { get; init; }

    public int PlayersWithValidProjection { get; init; }

    public int PlayersWithInjurySignal { get; init; }

    public int PlayersWithUsageSignal { get; init; }

    public int PlayersWithRoleSignal { get; init; }

    public required IReadOnlyList<string> UnavailableSources { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public string ToSummaryText()
    {
        var accuracy = DecisionAccuracyPercent is null
            ? "n/a"
            : $"{DecisionAccuracyPercent:0.#}%";
        var projErr = AverageProjectionAbsoluteError is null
            ? "n/a"
            : $"{AverageProjectionAbsoluteError:0.00}";
        var decVal = AverageDecisionDifferential is null
            ? "n/a"
            : $"{AverageDecisionDifferential:0.00}";

        var baseA = BaselineRecentAverageMae is null ? "n/a" : $"{BaselineRecentAverageMae:0.00}";
        var baseB = BaselineOpportunityAwareMae is null ? "n/a" : $"{BaselineOpportunityAwareMae:0.00}";

        return
            $"Replay: {Season} Week {Week}{Environment.NewLine}" +
            $"Cutoff: {InformationCutoff:u}{Environment.NewLine}" +
            $"Decisions: {DecisionCount}{Environment.NewLine}" +
            $"Correct: {CorrectCount}{Environment.NewLine}" +
            $"Incorrect: {IncorrectCount}{Environment.NewLine}" +
            $"Decision accuracy: {accuracy}{Environment.NewLine}" +
            $"Average projection error: {projErr}{Environment.NewLine}" +
            $"Baseline A MAE (recent avg): {baseA}{Environment.NewLine}" +
            $"Baseline B MAE (opportunity-aware): {baseB}{Environment.NewLine}" +
            $"Better baseline: {BetterBaselineLabel ?? "n/a"}{Environment.NewLine}" +
            $"Average decision value: {decVal}{Environment.NewLine}" +
            $"Average confidence: {AverageConfidence:0.#}";
    }
}
