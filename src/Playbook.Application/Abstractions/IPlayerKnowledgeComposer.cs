using Playbook.Core.Decisions;
using Playbook.Core.Players;

namespace Playbook.Application.Abstractions;

/// <summary>
/// Builds a structured <see cref="PlayerKnowledge"/> snapshot from available sources.
/// Does not invent data that is not present in those sources.
/// </summary>
public interface IPlayerKnowledgeComposer
{
    Task<PlayerKnowledge> ComposeAsync(
        Player player,
        DecisionContext context,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlayerKnowledge>> ComposeManyAsync(
        IEnumerable<Player> players,
        DecisionContext context,
        CancellationToken cancellationToken = default);
}
