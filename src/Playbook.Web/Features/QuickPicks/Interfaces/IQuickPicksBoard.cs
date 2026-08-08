using Playbook.Core.Predictions;

namespace Playbook.Web.Features.QuickPicks.Interfaces;

/// <summary>
/// Thin UI-facing board accessor. Domain work stays in Application/Infrastructure.
/// </summary>
public interface IQuickPicksBoard
{
    IReadOnlyList<Prediction> TopPicks { get; }

    IReadOnlyList<Prediction> WatchPicks { get; }

    IReadOnlyList<FootballEvent> Upcoming { get; }

    void Refresh();
}
