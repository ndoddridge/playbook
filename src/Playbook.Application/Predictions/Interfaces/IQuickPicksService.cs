using Playbook.Core.Predictions;

namespace Playbook.Application.Predictions.Interfaces;

public interface IQuickPicksService
{
    IReadOnlyList<Prediction> GetAllPredictions();

    IReadOnlyList<Prediction> GetTopPicks(int count = 8);

    IReadOnlyList<Prediction> GetWatchPicks(int count = 8);

    IReadOnlyList<FootballEvent> GetUpcomingEvents();

    void Refresh();
}
