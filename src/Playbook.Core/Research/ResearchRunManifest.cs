using System.Text.Json;
using System.Text.Json.Serialization;
using Playbook.Core.Replay;

namespace Playbook.Core.Research;

/// <summary>Reproducibility metadata for one research run artifact directory.</summary>
public sealed class ResearchRunManifest
{
    public required string RunId { get; init; }

    public required string Command { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    public required string? GitSha { get; init; }

    public required string? GitBranch { get; init; }

    public required string WorkingDirectory { get; init; }

    public required IReadOnlyList<string> Argv { get; init; }

    public required IReadOnlyList<int> Seasons { get; init; }

    public required ResearchScopeMode ScopeMode { get; init; }

    public required bool AllowHoldout { get; init; }

    public required bool UsedHoldoutForFitting { get; init; }

    public required ResearchPredictionSurface PredictionSurface { get; init; }

    public required ResearchKnowledgeModeLabel KnowledgeMode { get; init; }

    public required HistoricalCandidateUniverse CandidateUniverse { get; init; }

    public required string? ExperimentId { get; init; }

    public required int? Seed { get; init; }

    public required string OutputDirectory { get; init; }

    public required string ProductionKnowledgeMode { get; init; }

    public required IReadOnlyList<string> RejectedTransforms { get; init; }

    public required string? Verdict { get; init; }

    public required IReadOnlyDictionary<string, string> ArtifactPaths { get; init; }

    public required IReadOnlyDictionary<string, string> Notes { get; init; }

    public string ToJson(bool writeIndented = true) =>
        JsonSerializer.Serialize(this, writeIndented
            ? ResearchJson.Indented
            : ResearchJson.Compact);
}

/// <summary>Aggregate metrics captured for compare/report.</summary>
public sealed record ResearchRunMetrics
{
    public required string RunId { get; init; }

    public required string Surface { get; init; }

    public int? StartSitCandidates { get; init; }

    public int? StartSitGraded { get; init; }

    public double? StartSitAccuracyPercent { get; init; }

    public double? StartSitTotalDecisionValue { get; init; }

    public double? StartSitProjectionMae { get; init; }

    public int? QuickPickPredictions { get; init; }

    public double? QuickPickMae { get; init; }

    public double? QuickPickTop5Percent { get; init; }

    public double? QuickPickTop10Percent { get; init; }

    public int? RecommendationOrRankChanges { get; init; }

    public double? KnowledgeCoveragePercent { get; init; }

    public string? Verdict { get; init; }

    public IReadOnlyDictionary<int, double>? PerSeasonTotalDecisionValue { get; init; }

    public IReadOnlyDictionary<int, double>? PerSeasonQuickPickMae { get; init; }

    public string ToJson(bool writeIndented = true) =>
        JsonSerializer.Serialize(this, writeIndented
            ? ResearchJson.Indented
            : ResearchJson.Compact);
}

public sealed class ResearchCompareReport
{
    public required string LeftRunId { get; init; }

    public required string RightRunId { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required IReadOnlyList<string> Lines { get; init; }

    public string ToMarkdown() => string.Join(Environment.NewLine, Lines);
}

public static class ResearchJson
{
    // Write nulls so required nullable manifest fields round-trip.
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
