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

    IReadOnlyList<Prediction> TopPicks { get; }

    IReadOnlyList<Prediction> WatchPicks { get; }

    IReadOnlyList<Prediction> SlatePredictions { get; }

    IReadOnlyList<FootballEvent> Upcoming { get; }

    bool TrySelectWeek(NflWeekRef week);

    void Refresh();
}
