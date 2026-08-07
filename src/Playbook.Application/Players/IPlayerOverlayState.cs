using Playbook.Core.Players;

namespace Playbook.Application.Players;

/// <summary>
/// Application-wide player overlay state. Opens above the current page without navigation.
/// </summary>
public interface IPlayerOverlayState
{
    bool IsOpen { get; }

    Guid? SelectedPlayerId { get; }

    PlayerContext? Context { get; }

    event Action? Changed;

    void Open(Guid playerId);

    void Close();

    void Refresh();
}
