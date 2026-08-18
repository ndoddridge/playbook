namespace Playbook.Core.Draft;

/// <summary>"Can I get this player later?" — see <see cref="DraftAvailabilityPolicy"/>.</summary>
public enum AvailabilityRisk
{
    /// <summary>Not enough of this draft has happened yet to estimate a positional rate.</summary>
    Unknown = 0,

    /// <summary>Comparable players at this position are expected to still be there next turn.</summary>
    Safe = 1,

    /// <summary>Players this good at this position are unlikely to still be there next turn.</summary>
    AtRisk = 2
}

/// <summary>
/// Estimates pick timing / expected availability from THIS draft's own observed behaviour —
/// never from external ADP, which Playbook has no real source for (see the fabrication rule this
/// milestone was built under: unavailable signals are disclosed, never invented).
///
/// THE ESTIMATE: how many players other teams have actually taken at a position, per overall pick
/// made so far, is a real, observed rate for this specific draft. Projecting that rate forward
/// across the picks between now and the user's next turn gives an expected count of same-position
/// players other teams will take in that window. If that expected count reaches or passes how
/// many players are ranked as good or better at the position (this player included), the player's
/// tier is not expected to survive.
///
/// This deliberately does not attempt to model WHICH team takes WHICH position (that would need
/// real per-team roster needs across all 12+ opponents, which Playbook cannot see) — only the
/// aggregate positional run rate this draft has actually shown so far.
/// </summary>
public static class DraftAvailabilityPolicy
{
    /// <summary>Picks that must have been made before a positional rate is trusted at all.</summary>
    public const int MinimumPicksForRate = 6;

    /// <summary>-1 is the "not enough history yet" sentinel consumed by <see cref="Classify"/>.</summary>
    public static decimal ObservedPositionRate(int picksAtPositionSoFar, int totalPicksSoFar)
    {
        if (totalPicksSoFar < MinimumPicksForRate)
        {
            return -1m;
        }

        return (decimal)Math.Max(0, picksAtPositionSoFar) / totalPicksSoFar;
    }

    /// <summary>
    /// <paramref name="positionalRank"/> is this player's 1-based rank among undrafted players at
    /// their own position (1 = best remaining) — a proxy for "how many players this good or
    /// better are left", since a positional run consumes the best remaining players first.
    /// </summary>
    public static AvailabilityRisk Classify(
        decimal observedPositionRate,
        int picksUntilNextUserPick,
        int positionalRank)
    {
        if (observedPositionRate < 0m)
        {
            return AvailabilityRisk.Unknown;
        }

        if (picksUntilNextUserPick <= 0)
        {
            return AvailabilityRisk.Safe;
        }

        var expectedDraftedAtPosition = observedPositionRate * picksUntilNextUserPick;
        return expectedDraftedAtPosition >= positionalRank ? AvailabilityRisk.AtRisk : AvailabilityRisk.Safe;
    }

    /// <summary>Plain-language, decision-oriented — no rates or ranks in the copy itself.</summary>
    public static string Describe(AvailabilityRisk risk, int picksUntilNextUserPick, string positionLabel) => risk switch
    {
        AvailabilityRisk.AtRisk when picksUntilNextUserPick == 1 =>
            $"Likely gone by your next pick — you're picking again in 1 pick.",
        AvailabilityRisk.AtRisk =>
            $"Based on this draft's pace, {positionLabel} at this level likely won't last the "
            + $"{picksUntilNextUserPick} picks until your next turn.",
        AvailabilityRisk.Safe when picksUntilNextUserPick <= 0 =>
            "You're on the clock now.",
        AvailabilityRisk.Safe =>
            $"Based on this draft's pace, comparable {positionLabel} options should still be there "
            + $"in {picksUntilNextUserPick} picks — safe to wait.",
        _ => "Not enough of this draft has happened yet to estimate whether this can wait."
    };
}
