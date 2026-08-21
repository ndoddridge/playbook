namespace Playbook.Core.Draft;

/// <summary>Deterministic look-ahead path if a candidate is drafted now.</summary>
public sealed class DraftLookAheadStep
{
    public required int Round { get; init; }

    public required string TargetPosition { get; init; }

    public required IReadOnlyList<string> LikelyTargets { get; init; }

    public required string Explanation { get; init; }
}

/// <summary>Compact pick slate entry with role, short reasons, and look-ahead.</summary>
public sealed class DraftPickRecommendation
{
    public required DraftRecommendation Player { get; init; }

    public required RecommendationRole Role { get; init; }

    public required IReadOnlyList<string> WhyBullets { get; init; }

    public required IReadOnlyList<DraftLookAheadStep> LookAhead { get; init; }

    public required decimal FitScore { get; init; }

    public required decimal StrategicScore { get; init; }

    public required decimal UrgencyScore { get; init; }

    public required decimal UpsideScore { get; init; }
}
