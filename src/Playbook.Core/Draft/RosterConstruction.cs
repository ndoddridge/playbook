namespace Playbook.Core.Draft;

/// <summary>
/// How heavily a roster is already invested at one position.
///
/// Finer-grained than <see cref="PositionalNeedLevel"/>, which collapses everything at or above
/// the starter requirement into "Satisfied". That distinction matters in dynasty: three good
/// receivers is useful depth, six is capital tied up in players who cannot all start.
/// </summary>
public enum RosterConstructionTier
{
    /// <summary>Nobody at this position at all.</summary>
    Empty = 0,

    /// <summary>Fewer bodies than starting slots.</summary>
    Light = 1,

    /// <summary>Starting slots covered, little behind them.</summary>
    Adequate = 2,

    /// <summary>Starters plus genuine depth — a healthy dynasty position.</summary>
    Depth = 3,

    /// <summary>More players than the position can realistically use.</summary>
    OverConcentrated = 4
}

/// <summary>
/// A bounded contextual nudge based on what the roster already looks like.
///
/// DESIGN CONSTRAINTS, deliberately conservative:
///   - This REFINES the existing positional-need signal, it does not replace it. Need already
///     handles "urgent vs satisfied"; this adds the distinction between useful depth and
///     over-investment, which need cannot express.
///   - Total effect is clamped to +/-2.5 points, smaller than the existing need bonus (3.0) and
///     far smaller than realistic projection gaps. A clearly better player cannot lose on roster
///     construction alone.
///   - No rule of the form "never draft a fourth WR". Dynasty rosters need depth and value.
///
/// The weights are product judgements, not fitted values. There is no dataset of correct dynasty
/// roster shapes to calibrate against, and pretending otherwise would be false precision.
/// </summary>
public static class RosterConstructionPolicy
{
    public const decimal MaxAdjustment = 2.5m;

    /// <summary>
    /// Classify from real counts. <paramref name="targetStarters"/> comes from the league's own
    /// roster settings, so this adapts to superflex, 3-WR leagues and so on rather than assuming
    /// a shape.
    /// </summary>
    public static RosterConstructionTier Classify(int currentCount, int targetStarters)
    {
        var target = Math.Max(1, targetStarters);

        if (currentCount <= 0)
        {
            return RosterConstructionTier.Empty;
        }

        if (currentCount < target)
        {
            return RosterConstructionTier.Light;
        }

        if (currentCount == target)
        {
            return RosterConstructionTier.Adequate;
        }

        // One or two beyond the starting requirement is the useful-depth band. Beyond roughly
        // double the requirement, additional bodies stop being usable.
        return currentCount >= target * 2 + 1
            ? RosterConstructionTier.OverConcentrated
            : RosterConstructionTier.Depth;
    }

    /// <summary>
    /// Base adjustment before phase and strategy weighting. Positive where the roster can still
    /// use the position, negative only when genuinely over-invested.
    /// </summary>
    public static decimal BaseAdjustment(RosterConstructionTier tier) => tier switch
    {
        RosterConstructionTier.Empty => 2.0m,
        RosterConstructionTier.Light => 1.2m,
        RosterConstructionTier.Adequate => 0.0m,
        RosterConstructionTier.Depth => -0.4m,
        RosterConstructionTier.OverConcentrated => -2.0m,
        _ => 0m
    };

    /// <summary>Final adjustment, phase-weighted and clamped.</summary>
    public static decimal Adjustment(RosterConstructionTier tier, decimal phaseWeight)
    {
        var raw = BaseAdjustment(tier) * phaseWeight;
        return Math.Clamp(raw, -MaxAdjustment, MaxAdjustment);
    }

    /// <summary>Plain-language description for the recommendation card. No internal jargon.</summary>
    public static string Describe(RosterConstructionTier tier, string positionLabel) => tier switch
    {
        RosterConstructionTier.Empty => $"You have no {positionLabel} yet.",
        RosterConstructionTier.Light => $"You're still short of starting {positionLabel}.",
        RosterConstructionTier.Adequate => $"Your {positionLabel} starters are covered.",
        RosterConstructionTier.Depth => $"You already have useful {positionLabel} depth.",
        RosterConstructionTier.OverConcentrated =>
            $"You're heavily invested at {positionLabel} — more here is hard to use.",
        _ => ""
    };
}
