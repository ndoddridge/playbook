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

    /// <summary>
    /// First pick number at or after <paramref name="fromPickNumber"/> (inclusive) whose slot
    /// belongs to <paramref name="rosterId"/> — i.e. the user's actual next selection, correctly
    /// accounting for snake reversals rather than assuming a fixed team count or "next round".
    /// Bounded by <paramref name="totalPicks"/> (rounds * teams) so an unmapped roster can never
    /// loop forever. Returns null when the roster never comes on the clock within that range.
    /// </summary>
    public static int? NextPickForRoster(
        int fromPickNumber,
        int teamCount,
        bool isSnake,
        int totalPicks,
        int rosterId,
        IReadOnlyDictionary<int, int> slotToRosterId)
    {
        if (teamCount <= 0 || rosterId == 0)
        {
            return null;
        }

        for (var pick = Math.Max(1, fromPickNumber); pick <= totalPicks; pick++)
        {
            var slot = SlotForPick(pick, teamCount, isSnake);
            if (slotToRosterId.TryGetValue(slot, out var candidateRosterId) && candidateRosterId == rosterId)
            {
                return pick;
            }
        }

        return null;
    }
}
