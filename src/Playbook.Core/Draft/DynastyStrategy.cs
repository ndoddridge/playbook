namespace Playbook.Core.Draft;

/// <summary>
/// How a dynasty team wants the draft board weighted. Redraft leagues have no equivalent —
/// every pick is a win-now pick — so this is only surfaced and applied for dynasty.
/// </summary>
public enum DynastyStrategy
{
    /// <summary>Win now. Production and roster fit lead; age barely matters.</summary>
    ChampionshipCompetitor = 0,

    /// <summary>Balance current production against age and future value.</summary>
    Hybrid = 1,

    /// <summary>Build for later. Youth and ascending profiles lead over present output.</summary>
    Rebuild = 2
}

/// <summary>
/// Weights that turn a <see cref="DynastyStrategy"/> into actual scoring behaviour.
///
/// These are DELIBERATE PRODUCT JUDGEMENTS, not fitted values, and are documented as such. They
/// express how much a manager in each posture should care about age relative to production —
/// there is no historical dataset of "correct" dynasty draft picks to fit against, so claiming a
/// calibrated number here would be false precision.
///
/// The one hard rule encoded below: age adjustments are bounded so they can shade a close call
/// but can never overturn a large production gap. A 21-year-old who cannot play does not out-rank
/// a genuine starter in any posture.
/// </summary>
public static class DynastyStrategyPolicy
{
    /// <summary>Bonus per year under 27. Rebuild rewards youth hardest.</summary>
    public static decimal YouthBonusPerYear(DynastyStrategy strategy) => strategy switch
    {
        DynastyStrategy.ChampionshipCompetitor => 0.04m,
        DynastyStrategy.Hybrid => 0.15m,
        DynastyStrategy.Rebuild => 0.45m,
        _ => 0.15m
    };

    /// <summary>Penalty per year over 29. Competitors tolerate age; rebuilds do not.</summary>
    public static decimal AgePenaltyPerYear(DynastyStrategy strategy) => strategy switch
    {
        DynastyStrategy.ChampionshipCompetitor => 0.05m,
        DynastyStrategy.Hybrid => 0.15m,
        DynastyStrategy.Rebuild => 0.55m,
        _ => 0.15m
    };

    /// <summary>
    /// Extra weight on immediate roster need. A contender filling a hole this year values it more
    /// than a rebuilding team, which can afford to take the better long-term asset.
    /// </summary>
    public static decimal NeedWeightMultiplier(DynastyStrategy strategy) => strategy switch
    {
        DynastyStrategy.ChampionshipCompetitor => 1.25m,
        DynastyStrategy.Hybrid => 1.0m,
        DynastyStrategy.Rebuild => 0.6m,
        _ => 1.0m
    };

    /// <summary>
    /// Total age adjustment, bounded. The cap is the safeguard that stops an age curve from
    /// overwhelming a real production difference.
    /// </summary>
    public const decimal MaxAgeAdjustment = 6.0m;

    public static decimal AgeAdjustment(DynastyStrategy strategy, int age)
    {
        var raw = age < 27
            ? (27 - age) * YouthBonusPerYear(strategy)
            : age > 29
                ? -(age - 29) * AgePenaltyPerYear(strategy)
                : 0m;

        return Math.Clamp(raw, -MaxAgeAdjustment, MaxAgeAdjustment);
    }

    public static string DisplayName(DynastyStrategy strategy) => strategy switch
    {
        DynastyStrategy.ChampionshipCompetitor => "Championship Competitor",
        DynastyStrategy.Hybrid => "Hybrid",
        DynastyStrategy.Rebuild => "Rebuild",
        _ => "Hybrid"
    };

    public static string Description(DynastyStrategy strategy) => strategy switch
    {
        DynastyStrategy.ChampionshipCompetitor =>
            "Win now — production and roster fit lead, age is nearly ignored.",
        DynastyStrategy.Hybrid =>
            "Balance this season's production against age and future value.",
        DynastyStrategy.Rebuild =>
            "Build for later — youth and ascending profiles lead over present output.",
        _ => ""
    };
}
