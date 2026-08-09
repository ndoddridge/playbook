using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Web.Features.QuickPicks.Interfaces;

namespace Playbook.Web.Features.QuickPicks.Services;

public sealed class QuickPicksBoard : IQuickPicksBoard
{
    private readonly IQuickPicksService _service;

    public QuickPicksBoard(IQuickPicksService service)
    {
        _service = service;
    }

    public NflWeekRef? SelectedWeek => _service.SelectedWeek;

    public IReadOnlyList<NflWeekRef> AvailableWeeks => _service.AvailableWeeks;

    public NflSeasonContext? SeasonContext => _service.SeasonContext;

    public IReadOnlyList<Prediction> TopPicks => _service.GetTopPicks(8);

    public IReadOnlyList<Prediction> WatchPicks => _service.GetWatchPicks(8);

    public IReadOnlyList<FootballEvent> Upcoming => _service.GetUpcomingEvents();

    public bool TrySelectWeek(NflWeekRef week) => _service.TrySelectWeek(week);

    public void Refresh() => _service.Refresh();
}
