using Playbook.Core.Predictions;

namespace Playbook.Application.Predictions.Interfaces;

public interface IQuickPicksService
{
    /// <summary>Active NFL week slate currently driving the board.</summary>
    NflWeekRef? SelectedWeek { get; }

    /// <summary>Weeks present in the loaded prop slate (for a future week selector).</summary>
    IReadOnlyList<NflWeekRef> AvailableWeeks { get; }

    /// <summary>Resolved live NFL calendar context (season / phase / week).</summary>
    global::Playbook.Application.Predictions.NflSeasonContext? SeasonContext { get; }

    IReadOnlyList<Prediction> GetAllPredictions();

    IReadOnlyList<Prediction> GetTopPicks(int count = 8);

    IReadOnlyList<Prediction> GetWatchPicks(int count = 8);

    IReadOnlyList<FootballEvent> GetUpcomingEvents();

    /// <summary>
    /// Select a week slate when it exists in <see cref="AvailableWeeks"/>.
    /// Rebuilds predictions for that week only. Returns false if the week is unavailable.
    /// </summary>
    bool TrySelectWeek(NflWeekRef week);

    void Refresh();
}
