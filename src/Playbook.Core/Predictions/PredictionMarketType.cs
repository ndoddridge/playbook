namespace Playbook.Core.Predictions;

/// <summary>
/// Football prediction markets for Quick Picks (independent of fantasy scoring).
/// </summary>
public enum PredictionMarketType
{
    PassingYards = 0,
    RushingYards = 1,
    ReceivingYards = 2,
    Receptions = 3,
    AnytimeTouchdown = 4,
    PassingTouchdowns = 5,
    TeamTotal = 6,
    GameTotal = 7,
    Winner = 8,
    Spread = 9
}
