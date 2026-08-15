namespace Playbook.Core.Draft;

/// <summary>Pure snake/linear draft-order math — no Sleeper or Playbook types involved, so it's
/// trivially testable and reusable regardless of data source.</summary>
public static class DraftOrderCalculator
{
    /// <summary>1-indexed draft slot on the clock for the given 1-indexed overall pick number.</summary>
    public static int SlotForPick(int pickNumber, int teamCount, bool isSnake)
    {
        if (teamCount <= 0 || pickNumber <= 0)
        {
            return 0;
        }

        var zeroBasedPick = pickNumber - 1;
        var round = zeroBasedPick / teamCount;
        var positionInRound = zeroBasedPick % teamCount;
        var reversed = isSnake && round % 2 == 1;
        var slotZeroBased = reversed ? teamCount - 1 - positionInRound : positionInRound;
        return slotZeroBased + 1;
    }

    public static int RoundForPick(int pickNumber, int teamCount) =>
        teamCount <= 0 || pickNumber <= 0 ? 0 : ((pickNumber - 1) / teamCount) + 1;
}
