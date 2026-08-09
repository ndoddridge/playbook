using Playbook.Core.Injuries.Models;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;

namespace Playbook.Core.Intelligence.Models;

/// <summary>
/// Composed, user-facing player intelligence view.
/// Built from existing profile / injury / projection / stats / news — never invents data.
/// </summary>
public sealed class PlayerIntelligenceAssessment
{
    public required Guid PlayerId { get; init; }

    public required PlayerOutlook Outlook { get; init; }

    public required string OutlookLabel { get; init; }

    public required string Headline { get; init; }

    public required int AssessmentConfidence { get; init; }

    public required string ConfidenceNote { get; init; }

    public required int? OpportunityScore { get; init; }

    public required int? UsageScore { get; init; }

    public required int? HealthScore { get; init; }

    public required string HealthStatusLabel { get; init; }

    public required string? ProjectionSummary { get; init; }

    public required IReadOnlyList<IntelligenceFactor> PositiveFactors { get; init; }

    public required IReadOnlyList<IntelligenceFactor> NegativeFactors { get; init; }

    public required IReadOnlyList<RecentIntelligenceItem> RecentIntelligence { get; init; }

    public required IReadOnlyList<string> UnavailableSignals { get; init; }

    public required IReadOnlyList<AssessmentDetailSection> DetailSections { get; init; }

    public PlayerIntelligenceProfile? Profile { get; init; }

    public PlayerInjuryProfile? InjuryProfile { get; init; }

    public PlayerProjection? Projection { get; init; }

    public PlayerStatisticalContext? StatisticalContext { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }
}

public enum PlayerOutlook
{
    Strong = 0,
    Positive = 1,
    Neutral = 2,
    Concerning = 3,
    Unknown = 4
}

public sealed class IntelligenceFactor
{
    public required string Text { get; init; }

    public required string? Detail { get; init; }

    public required string Source { get; init; }
}

public sealed class RecentIntelligenceItem
{
    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required bool IsConfirmed { get; init; }

    public required string VerificationLabel { get; init; }

    public required string Category { get; init; }

    public required string Source { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public string? Url { get; init; }
}

public sealed record AssessmentDetailSection(string Title, IReadOnlyList<string> Items);
