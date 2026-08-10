using System.Text;

namespace Playbook.Core.Replay;

/// <summary>
/// Frozen coverage-expansion measurement protocol.
/// Counts only — no model fitting, no 2024-based tuning.
/// </summary>
public static class FrozenHistoricalEvaluationCoverageV1
{
    public const string ProtocolId = "historical-evaluation-coverage-v1";
    public const int DevelopmentSeason = 2018;
    public const int DevelopmentStartWeek = 1;
    public const int DevelopmentEndWeek = 17;
    public const int HoldoutSeason = 2024;
    public const int HoldoutStartWeek = 1;
    public const int HoldoutEndWeek = 17;
}

public sealed class HistoricalCoverageSliceCounts
{
    public required HistoricalCandidateUniverse Universe { get; init; }

    public required int WeeksLoaded { get; init; }

    public required int DistinctPlayers { get; init; }

    public required int PlayerWeeks { get; init; }

    public required int StartSitCandidates { get; init; }

    public required int StartSitPredictions { get; init; }

    public required int StartSitGradedPredictions { get; init; }

    public required int QuickPickCandidates { get; init; }

    public required int QuickPickPredictions { get; init; }

    public required int QuickPickGradedPredictions { get; init; }

    public required int PlayersWithValidProjection { get; init; }

    public required int PlayersWithWeekOutcome { get; init; }

    public required IReadOnlyDictionary<HistoricalCoverageExclusionReason, int> ExclusionsByReason { get; init; }
}

public sealed class HistoricalCoverageSeasonCompare
{
    public required int Season { get; init; }

    public required int StartWeek { get; init; }

    public required int EndWeek { get; init; }

    public required string Role { get; init; }

    public required HistoricalCoverageSliceCounts Before { get; init; }

    public required HistoricalCoverageSliceCounts After { get; init; }
}

public sealed class HistoricalEvaluationCoverageReport
{
    public required string ProtocolId { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required HistoricalCoverageSeasonCompare Development { get; init; }

    public required HistoricalCoverageSeasonCompare Holdout { get; init; }

    public required bool HoldoutIsolated { get; init; }

    public required bool Frozen2018BenchmarkUnchanged { get; init; }

    public string ToReportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Historical Evaluation Coverage Report — {ProtocolId}");
        sb.AppendLine($"GeneratedAt: {GeneratedAt:u}");
        sb.AppendLine($"HoldoutIsolated: {HoldoutIsolated}");
        sb.AppendLine($"Frozen2018BenchmarkUnchanged: {Frozen2018BenchmarkUnchanged}");
        sb.AppendLine();
        AppendSeason(sb, Development);
        sb.AppendLine();
        AppendSeason(sb, Holdout);
        return sb.ToString();
    }

    private static void AppendSeason(StringBuilder sb, HistoricalCoverageSeasonCompare season)
    {
        sb.AppendLine($"## {season.Role}: {season.Season} W{season.StartWeek}-{season.EndWeek}");
        sb.AppendLine();
        sb.AppendLine("| Metric | BEFORE (LabRoster) | AFTER (ExpandedSkillUniverse) | Δ |");
        sb.AppendLine("|---|---:|---:|---:|");
        Row(sb, "Weeks loaded", season.Before.WeeksLoaded, season.After.WeeksLoaded);
        Row(sb, "Distinct players", season.Before.DistinctPlayers, season.After.DistinctPlayers);
        Row(sb, "Player-weeks", season.Before.PlayerWeeks, season.After.PlayerWeeks);
        Row(sb, "Players w/ valid projection", season.Before.PlayersWithValidProjection, season.After.PlayersWithValidProjection);
        Row(sb, "Players w/ week outcome", season.Before.PlayersWithWeekOutcome, season.After.PlayersWithWeekOutcome);
        Row(sb, "Start/Sit candidates", season.Before.StartSitCandidates, season.After.StartSitCandidates);
        Row(sb, "Start/Sit predictions", season.Before.StartSitPredictions, season.After.StartSitPredictions);
        Row(sb, "Start/Sit graded", season.Before.StartSitGradedPredictions, season.After.StartSitGradedPredictions);
        Row(sb, "Quick Picks candidates", season.Before.QuickPickCandidates, season.After.QuickPickCandidates);
        Row(sb, "Quick Picks predictions", season.Before.QuickPickPredictions, season.After.QuickPickPredictions);
        Row(sb, "Quick Picks graded", season.Before.QuickPickGradedPredictions, season.After.QuickPickGradedPredictions);
        sb.AppendLine();
        sb.AppendLine("### Exclusions by reason (AFTER − BEFORE where applicable)");
        sb.AppendLine();
        sb.AppendLine("| Reason | BEFORE | AFTER |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var reason in Enum.GetValues<HistoricalCoverageExclusionReason>())
        {
            season.Before.ExclusionsByReason.TryGetValue(reason, out var b);
            season.After.ExclusionsByReason.TryGetValue(reason, out var a);
            sb.AppendLine($"| {reason} | {b} | {a} |");
        }
    }

    private static void Row(StringBuilder sb, string name, int before, int after) =>
        sb.AppendLine($"| {name} | {before} | {after} | {after - before:+0;-0;0} |");
}
