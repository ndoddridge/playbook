namespace Playbook.Core.Players;

/// <summary>
/// Placeholder career aggregate statistics.
/// </summary>
public sealed class CareerStats
{
    public int Seasons { get; init; }

    public int GamesPlayed { get; init; }

    public decimal FantasyPoints { get; init; }

    public int PassingYards { get; init; }

    public int RushingYards { get; init; }

    public int ReceivingYards { get; init; }

    public int TotalTouchdowns { get; init; }
}
