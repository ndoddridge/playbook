namespace Playbook.Core.Projections.Models;

/// <summary>
/// Side-by-side projection comparison for future Decision Engine consumers.
/// </summary>
public sealed class PlayerProjectionComparison
{
    public required PlayerProjection Left { get; init; }

    public required PlayerProjection Right { get; init; }

    public decimal ProjectedPointsDelta =>
        Left.ProjectedFantasyPoints - Right.ProjectedFantasyPoints;

    public int ConfidenceDelta => Left.Confidence - Right.Confidence;

    public int VolatilityDelta => Left.Volatility - Right.Volatility;

    public required IReadOnlyList<string> ComparisonNotes { get; init; }
}
