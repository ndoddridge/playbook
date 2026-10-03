using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Web.Features.QuickPicks.Interfaces;

namespace Playbook.Web.Features.QuickPicks.Services;

/// <summary>
/// Per-user (scoped) view over the shared Quick Picks engine. The engine tracks one "selected week";
/// each user's board remembers their own choice and re-applies it, under a shared gate, before every
/// read — so one customer switching weeks never changes what another customer sees.
/// </summary>
public sealed class QuickPicksBoard : IQuickPicksBoard
{
    private static readonly object Gate = new();

    private readonly IQuickPicksService _service;
    private NflWeekRef? _myWeek;

    public QuickPicksBoard(IQuickPicksService service)
    {
        _service = service;
    }

    private T Read<T>(Func<T> read)
    {
        lock (Gate)
        {
            if (_myWeek is not null && !Equals(_service.SelectedWeek, _myWeek))
            {
                _service.TrySelectWeek(_myWeek);
            }

            return read();
        }
    }

    public NflWeekRef? SelectedWeek => Read(() => _service.SelectedWeek);

    public IReadOnlyList<NflSlate> AvailableSlates => Read(() => _service.AvailableSlates);

    public IReadOnlyList<NflWeekRef> AvailableWeeks => Read(() => _service.AvailableWeeks);

    public IReadOnlyList<NflWeekRef> CanonicalWeeks => Read(() => _service.CanonicalWeeks);

    public NflSeasonContext? SeasonContext => Read(() => _service.SeasonContext);

    public IReadOnlyList<Prediction> GetTopPicks(int count) => Read(() => _service.GetTopPicks(count));

    public IReadOnlyList<Prediction> GetWatchPicks(int count, int topCount) =>
        Read(() => _service.GetWatchPicks(count, topCount));

    public IReadOnlyList<Prediction> SlatePredictions => Read(() => _service.GetSlatePredictions());

    public IReadOnlyList<FootballEvent> Upcoming => Read(() => _service.GetUpcomingEvents());

    public (int GameMarketLines, int PlayerPropLines) GetSlateMarketCounts() =>
        Read(() => _service.GetSlateMarketCounts());

    public IReadOnlyList<Playbook.Core.Predictions.Models.GameMarketAnalysis> GetGameMarketAnalyses() =>
        Read(() => _service.GetGameMarketAnalyses());

    public bool TrySelectWeek(NflWeekRef week)
    {
        lock (Gate)
        {
            var ok = _service.TrySelectWeek(week);
            if (ok)
            {
                _myWeek = week;
            }

            return ok;
        }
    }

    public void Refresh()
    {
        lock (Gate)
        {
            _service.Refresh();
        }
    }
}
