namespace Playbook.Application.Players.Data;

/// <summary>
/// Configured player catalog source. Switch via <c>PlayerData:Provider</c> — no code changes required.
/// </summary>
public enum PlayerDataProviderKind
{
    Mock = 0,
    Live = 1
}
