using Playbook.Core.Historical;

namespace Playbook.Application.Historical;

public interface IHistoricalLeagueDraftStore
{
    IReadOnlyList<HistoricalLeagueDraft> Load();
    void Save(IReadOnlyList<HistoricalLeagueDraft> drafts);
}
