namespace Playbook.Core.Draft;

public enum PositionalNeedLevel
{
    Satisfied,
    Moderate,
    Urgent
}

/// <summary>This team's real, currently-drafted depth at a position vs. a reasonable starter
/// target — drawn entirely from actual picks made so far and the league's roster settings.</summary>
public sealed class PositionalNeed
{
    public required string PositionLabel { get; init; }

    public required int CurrentCount { get; init; }

    public required int TargetStarters { get; init; }

    public required PositionalNeedLevel NeedLevel { get; init; }
}
