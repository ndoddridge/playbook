using Playbook.Core.Injuries.Models;

namespace Playbook.Application.Injuries;

public static class InjuryAvailabilityPresentation
{
    public static string HistoricalMessage(HistoricalDataStatus status) => status switch
    {
        HistoricalDataStatus.Available => "Historical injury records are available.",
        HistoricalDataStatus.NoRecordsFound =>
            "No historical injury records were returned for this player by the historical provider.",
        HistoricalDataStatus.NotSupportedByProvider =>
            "Historical injury data is not available from the current provider.",
        HistoricalDataStatus.Unavailable =>
            "Historical injury data is temporarily unavailable.",
        HistoricalDataStatus.NotSynced =>
            "Historical injury data has not been synced yet.",
        _ => status.ToString()
    };

    public static string CurrentStatusLabel(CurrentInjuryDataStatus status) => status switch
    {
        CurrentInjuryDataStatus.Available => "Designation available",
        CurrentInjuryDataStatus.NoCurrentInjury => "No current designation",
        CurrentInjuryDataStatus.Unavailable => "Current data unavailable",
        CurrentInjuryDataStatus.NotSynced => "Not synced",
        CurrentInjuryDataStatus.MappingFailed => "Mapping failed",
        _ => status.ToString()
    };

    public static string CollegeMessage(HistoricalDataStatus status) => status switch
    {
        HistoricalDataStatus.Available => "College injury records are available.",
        HistoricalDataStatus.NoRecordsFound =>
            "No college injury records were returned for this player by the college injury provider.",
        HistoricalDataStatus.NotSupportedByProvider =>
            "College injury data is not available from the current provider.",
        HistoricalDataStatus.Unavailable =>
            "College injury data is temporarily unavailable.",
        HistoricalDataStatus.NotSynced =>
            "College injury data has not been synced yet.",
        _ => status.ToString()
    };
}
