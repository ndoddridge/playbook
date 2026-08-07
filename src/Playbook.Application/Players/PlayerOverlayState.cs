using Playbook.Application.Leagues;
using Playbook.Core.Players;

namespace Playbook.Application.Players;

public sealed class PlayerOverlayState : IPlayerOverlayState, IDisposable
{
    private readonly IPlayerContextService _contextService;
    private readonly ILeagueState _leagueState;

    public PlayerOverlayState(IPlayerContextService contextService, ILeagueState leagueState)
    {
        _contextService = contextService;
        _leagueState = leagueState;
        _leagueState.Changed += OnLeagueChanged;
    }

    public bool IsOpen { get; private set; }

    public Guid? SelectedPlayerId { get; private set; }

    public PlayerContext? Context { get; private set; }

    public event Action? Changed;

    public void Open(Guid playerId)
    {
        SelectedPlayerId = playerId;
        Context = _contextService.GetContext(playerId);
        IsOpen = Context is not null;
        Changed?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        Changed?.Invoke();
    }

    public void Refresh()
    {
        if (SelectedPlayerId is not Guid playerId)
        {
            return;
        }

        Context = _contextService.GetContext(playerId);
        Changed?.Invoke();
    }

    private void OnLeagueChanged()
    {
        if (IsOpen)
        {
            Refresh();
        }
    }

    public void Dispose()
    {
        _leagueState.Changed -= OnLeagueChanged;
    }
}
