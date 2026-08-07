using Playbook.Core.Players;

namespace Playbook.Application.Players.Data;

/// <summary>
/// Abstraction for raw player catalog ingestion. UI never talks to providers directly —
/// <see cref="IPlayerService"/> selects Mock or Live from configuration and maps into domain models.
/// </summary>
public interface IPlayerDataProvider
{
    PlayerDataProviderKind Kind { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<Player>> GetPlayersAsync(CancellationToken cancellationToken = default);
}
