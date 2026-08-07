namespace Playbook.Core.Players;

/// <summary>
/// Placeholder season statistics structure for future Data Engine population.
/// </summary>
public sealed class SeasonStats
{
    public required int Season { get; init; }

    public int GamesPlayed { get; init; }

    public decimal FantasyPoints { get; init; }

    public int PassingYards { get; init; }

    public int PassingTouchdowns { get; init; }

    public int RushingYards { get; init; }

    public int RushingTouchdowns { get; init; }

    public int Receptions { get; init; }

    public int ReceivingYards { get; init; }

    public int ReceivingTouchdowns { get; init; }

    public int Targets { get; init; }
}
