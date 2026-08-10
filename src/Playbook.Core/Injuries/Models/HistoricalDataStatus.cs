namespace Playbook.Core.Injuries.Models;

/// <summary>
/// Explicit availability of historical injury records for a player.
/// Missing history must never be interpreted as "never injured."
/// </summary>
public enum HistoricalDataStatus
{
    Available = 0,
    Unavailable = 1,
    NotSupportedByProvider = 2,
    NotSynced = 3,
    NoRecordsFound = 4
}

/// <summary>
/// Explicit availability of a current injury designation.
/// </summary>
public enum CurrentInjuryDataStatus
{
    /// <summary>Provider returned a current designation (including Active recovery notes).</summary>
    Available = 0,

    /// <summary>Provider supports current data and returned no active designation for this player.</summary>
    NoCurrentInjury = 1,

    /// <summary>Current data could not be loaded (provider/network failure).</summary>
    Unavailable = 2,

    /// <summary>Injury sync has not completed yet.</summary>
    NotSynced = 3,

    /// <summary>Provider had a designation but it could not be mapped to a Playbook player.</summary>
    MappingFailed = 4
}
