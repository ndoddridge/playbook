using Playbook.Core.Draft;

namespace Playbook.Application.Historical;

/// <summary>
/// File-backed personal draft knowledge, scoped per LeagueId + TeamId. Same persistence volume
/// as <see cref="IHistoricalLeagueDraftStore"/> — not a parallel draft architecture.
/// </summary>
public interface IPersonalDraftKnowledgeStore
{
    IReadOnlyList<PersonalDraftKnowledge> Load();
    void Save(IReadOnlyList<PersonalDraftKnowledge> knowledge);
}
