using Playbook.Core.Predictions;

namespace Playbook.Application.Predictions.Interfaces;

public interface IQuickPicksService
{
    /// <summary>Active NFL slate currently driving the board.</summary>
    NflWeekRef? SelectedWeek { get; }

    /// <summary>Provider-backed slates that have at least one real game/market.</summary>
    IReadOnlyList<NflSlate> AvailableSlates { get; }

    /// <summary>Slate refs present in the loaded prop catalog (provider-backed).</summary>
    IReadOnlyList<NflWeekRef> AvailableWeeks { get; }

    /// <summary>
    /// Full canonical NFL season structure for the active season
    /// (Preseason 1–3, Regular 1–18, Postseason rounds). Used by the week picker.
    /// </summary>
    IReadOnlyList<NflWeekRef> CanonicalWeeks { get; }

    /// <summary>Resolved live NFL calendar context (season / phase hint).</summary>
    global::Playbook.Application.Predictions.NflSeasonContext? SeasonContext { get; }

    IReadOnlyList<Prediction> GetAllPredictions();

    /// <summary>Strongest available eligible props on the selected slate (ranked, no confidence floor).</summary>
    IReadOnlyList<Prediction> GetTopPicks(int count = 5);

    /// <summary>Next opportunities after Top Picks on the same ranking.</summary>
    IReadOnlyList<Prediction> GetWatchPicks(int count = 8, int topCount = 5);

    /// <summary>All evaluated props for the selected slate (for "All Props" / filtering).</summary>
    IReadOnlyList<Prediction> GetSlatePredictions();

    IReadOnlyList<FootballEvent> GetUpcomingEvents();

    /// <summary>
    /// Select a canonical season slate. Rebuilds predictions for that slate only
    /// (empty when the provider has no markets for it). Returns false if out of season.
    /// </summary>
    bool TrySelectWeek(NflWeekRef week);

    void Refresh();
}
