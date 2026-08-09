using Playbook.Application.Predictions;
using Playbook.Core.Predictions;

namespace Playbook.Web.Features.QuickPicks.Interfaces;

/// <summary>
/// Thin UI-facing board accessor. Domain work stays in Application/Infrastructure.
/// Structured so a Week/Game selector can bind to AvailableWeeks / SelectedWeek later.
/// </summary>
public interface IQuickPicksBoard
{
    NflWeekRef? SelectedWeek { get; }

    IReadOnlyList<NflWeekRef> AvailableWeeks { get; }

    NflSeasonContext? SeasonContext { get; }

    IReadOnlyList<Prediction> TopPicks { get; }

    IReadOnlyList<Prediction> WatchPicks { get; }

    IReadOnlyList<FootballEvent> Upcoming { get; }

    bool TrySelectWeek(NflWeekRef week);

    void Refresh();
}
