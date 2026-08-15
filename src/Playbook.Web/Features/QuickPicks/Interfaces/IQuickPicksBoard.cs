using Playbook.Application.Predictions;
using Playbook.Core.Predictions;

namespace Playbook.Web.Features.QuickPicks.Interfaces;

/// <summary>
/// Thin UI-facing board accessor. Domain work stays in Application/Infrastructure.
/// </summary>
public interface IQuickPicksBoard
{
    NflWeekRef? SelectedWeek { get; }

    IReadOnlyList<NflSlate> AvailableSlates { get; }

    IReadOnlyList<NflWeekRef> AvailableWeeks { get; }

    IReadOnlyList<NflWeekRef> CanonicalWeeks { get; }

    NflSeasonContext? SeasonContext { get; }

    IReadOnlyList<Prediction> GetTopPicks(int count);

    IReadOnlyList<Prediction> GetWatchPicks(int count, int topCount);

    IReadOnlyList<Prediction> SlatePredictions { get; }

    IReadOnlyList<FootballEvent> Upcoming { get; }

    /// <summary>
    /// Real sportsbook lines on the selected slate, split by kind, so an empty board can be
    /// explained honestly rather than always blamed on player props.
    /// </summary>
    (int GameMarketLines, int PlayerPropLines) GetSlateMarketCounts();

    bool TrySelectWeek(NflWeekRef week);

    void Refresh();
}
