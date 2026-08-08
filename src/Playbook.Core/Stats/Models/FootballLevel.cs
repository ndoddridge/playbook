namespace Playbook.Core.Stats.Models;

/// <summary>
/// Statistical competition level. College and NFL must never be mixed into one sample.
/// </summary>
public enum FootballLevel
{
    Nfl = 0,
    College = 1,
    Career = 2
}

/// <summary>
/// Whether a provider row was resolved to a canonical Playbook player.
/// </summary>
public enum StatsIdentityMatch
{
    Matched = 0,
    Unresolved = 1,
    NotApplicable = 2
}

/// <summary>
/// Completeness of a normalized statistics record.
/// </summary>
public enum StatsCompleteness
{
    /// <summary>Core counting stats for the player's position are present (including zeros).</summary>
    Complete = 0,

    /// <summary>Some expected fields are missing (null), not merely zero.</summary>
    Partial = 1,

    /// <summary>Record exists but lacks usable counting stats.</summary>
    Sparse = 2
}
