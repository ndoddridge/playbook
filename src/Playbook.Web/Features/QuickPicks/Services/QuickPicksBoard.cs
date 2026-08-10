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

    public IReadOnlyList<NflSlate> AvailableSlates => _service.AvailableSlates;

    public IReadOnlyList<NflWeekRef> AvailableWeeks => _service.AvailableWeeks;

    public IReadOnlyList<NflWeekRef> CanonicalWeeks => _service.CanonicalWeeks;

    public NflSeasonContext? SeasonContext => _service.SeasonContext;

    public IReadOnlyList<Prediction> GetTopPicks(int count) => _service.GetTopPicks(count);

    public IReadOnlyList<Prediction> GetWatchPicks(int count, int topCount) =>
        _service.GetWatchPicks(count, topCount);

    public IReadOnlyList<Prediction> SlatePredictions => _service.GetSlatePredictions();

    public IReadOnlyList<FootballEvent> Upcoming => _service.GetUpcomingEvents();

    public bool TrySelectWeek(NflWeekRef week) => _service.TrySelectWeek(week);

    public void Refresh() => _service.Refresh();
}
