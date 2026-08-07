namespace Playbook.Core.Players;

/// <summary>
/// Aggregated player view for explorers and engines.
/// Future engines should request this instead of assembling pieces ad hoc.
/// </summary>
public sealed class PlayerProfile
{
    public required Player Player { get; init; }

    public SeasonStats? SeasonStats { get; init; }

    public CareerStats? CareerStats { get; init; }

    public CollegeStats? CollegeStats { get; init; }

    public IReadOnlyList<InjuryRecord> InjuryHistory { get; init; } = [];

    public PlayerTrend? Trend { get; init; }
}
