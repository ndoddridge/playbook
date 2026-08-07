using Playbook.Core.Players;

namespace Playbook.Application.Players;

/// <summary>
/// Builds league-aware player context. UI never invents fantasy values.
/// </summary>
public interface IPlayerContextService
{
    PlayerContext? GetContext(Guid playerId);
}
