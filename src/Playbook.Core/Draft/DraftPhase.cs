namespace Playbook.Core.Draft;

/// <summary>
/// Where a draft currently is. Derived from the real round count reported by Sleeper — never
/// assumed from league conventions.
/// </summary>
public enum DraftPhase
{
    Early = 0,
    Middle = 1,
    Late = 2
}

/// <summary>
/// Draft-phase detection and the weights each phase applies.
///
/// Phase LAYERS ON TOP of the dynasty strategy; it never overrides it. Strategy decides how much
/// a manager cares about age and need in principle; phase decides how much that matters right
/// now. A Rebuild stays youth-hungry in every phase — the phase only modulates the magnitude.
///
/// Thresholds are explicit fractions of the real total rounds so they adapt to a 15-round or a
/// 25-round dynasty draft without special-casing either. They are deliberate product judgements,
/// not learned values.
/// </summary>
public static class DraftPhasePolicy
{
    /// <summary>Rounds up to this fraction of the draft are Early.</summary>
    public const decimal EarlyThreshold = 1m / 3m;

    /// <summary>Rounds up to this fraction are Middle; beyond it, Late.</summary>
    public const decimal MiddleThreshold = 2m / 3m;

    /// <summary>
    /// Round number for a pick, 1-based. Mirrors the existing snake/linear-independent maths:
    /// round is purely a function of how many picks have elapsed.
    /// </summary>
    public static int RoundForPick(int pickNumber, int teamCount)
    {
        if (teamCount <= 0)
        {
            return 1;
        }

        return Math.Max(1, (Math.Max(1, pickNumber) - 1) / teamCount + 1);
    }

    /// <summary>
    /// Classify from the real round and the draft's real length. Falls back to Early when the
    /// total round count is unknown, which is the conservative choice — Early applies the
    /// smallest roster-construction nudge and so changes the board least.
    /// </summary>
    public static DraftPhase Classify(int round, int totalRounds)
    {
        if (totalRounds <= 0)
        {
            return DraftPhase.Early;
        }

        var fraction = (decimal)Math.Max(1, round) / totalRounds;

        if (fraction <= EarlyThreshold)
        {
            return DraftPhase.Early;
        }

        return fraction <= MiddleThreshold ? DraftPhase.Middle : DraftPhase.Late;
    }

    public static DraftPhase ClassifyFromPick(int pickNumber, int teamCount, int totalRounds) =>
        Classify(RoundForPick(pickNumber, teamCount), totalRounds);

    /// <summary>
    /// How much roster construction should matter.
    ///
    /// Early: low — take the best asset, roster shape can be fixed later.
    /// Middle: full — this is where construction genuinely drives picks.
    /// Late: moderate — depth still matters but upside starts to lead.
    /// </summary>
    public static decimal RosterConstructionWeight(DraftPhase phase) => phase switch
    {
        DraftPhase.Early => 0.4m,
        DraftPhase.Middle => 1.0m,
        DraftPhase.Late => 0.7m,
        _ => 1.0m
    };

    /// <summary>
    /// Multiplier applied to the dynasty age/upside curve. Late-round opportunity cost is low, so
    /// swinging at a young ascending player is cheaper — but the strategy still sets the base, so
    /// a Competitor late is still far less youth-driven than a Rebuild late.
    /// </summary>
    public static decimal UpsideWeight(DraftPhase phase) => phase switch
    {
        DraftPhase.Early => 1.0m,
        DraftPhase.Middle => 0.9m,
        DraftPhase.Late => 1.3m,
        _ => 1.0m
    };

    public static string DisplayName(DraftPhase phase) => phase switch
    {
        DraftPhase.Early => "Early draft",
        DraftPhase.Middle => "Middle draft",
        DraftPhase.Late => "Late draft",
        _ => "Draft"
    };

    /// <summary>Plain-language rationale for the card. Decision-oriented, no jargon.</summary>
    public static string Describe(DraftPhase phase) => phase switch
    {
        DraftPhase.Early => "Early picks favour the strongest long-term asset over roster shape.",
        DraftPhase.Middle => "Mid-draft picks weigh roster construction most heavily.",
        DraftPhase.Late => "Late picks favour upside — the cost of missing is low.",
        _ => ""
    };
}
