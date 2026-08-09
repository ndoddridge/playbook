using Playbook.Core.Predictions;

namespace Playbook.Application.Predictions.Interfaces;

public interface IQuickPicksService
{
    /// <summary>Active NFL slate currently driving the board.</summary>
    NflWeekRef? SelectedWeek { get; }

    /// <summary>Concrete available slates backed by real games (for navigator UI).</summary>
    IReadOnlyList<NflSlate> AvailableSlates { get; }

    /// <summary>Slate refs present in the loaded prop catalog.</summary>
    IReadOnlyList<NflWeekRef> AvailableWeeks { get; }

    /// <summary>Resolved live NFL calendar context (season / phase hint).</summary>
    global::Playbook.Application.Predictions.NflSeasonContext? SeasonContext { get; }

    IReadOnlyList<Prediction> GetAllPredictions();

    IReadOnlyList<Prediction> GetTopPicks(int count = 8);

    IReadOnlyList<Prediction> GetWatchPicks(int count = 8);

    /// <summary>All evaluated props for the selected slate (for "All Props" / filtering).</summary>
    IReadOnlyList<Prediction> GetSlatePredictions();

    IReadOnlyList<FootballEvent> GetUpcomingEvents();

    /// <summary>
    /// Select a slate when it exists in <see cref="AvailableWeeks"/>.
    /// Rebuilds predictions for that slate only. Returns false if unavailable.
    /// </summary>
    bool TrySelectWeek(NflWeekRef week);

    void Refresh();
}
