using Playbook.Core.Decisions;
using Playbook.Core.Replay;

namespace Playbook.Application.Replay;

/// <summary>
/// Builds <see cref="PlayerKnowledge"/> from a historical snapshot only.
/// Does not call live assessment/news/injury services.
/// </summary>
public interface IHistoricalKnowledgeFactory
{
    IReadOnlyList<PlayerKnowledge> BuildKnowledge(HistoricalSnapshot snapshot, DecisionContext context);
}
