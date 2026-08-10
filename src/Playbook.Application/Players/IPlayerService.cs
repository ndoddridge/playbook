using Playbook.Core.Players;

namespace Playbook.Application.Players;

/// <summary>
/// Single source of player domain data. UI never constructs players.
/// </summary>
public interface IPlayerService
{
    IReadOnlyList<Player> GetAllPlayers();

    Player? GetPlayer(Guid playerId);

    PlayerProfile? GetPlayerProfile(Guid playerId);

    IReadOnlyList<Player> SearchPlayers(string? query);

    /// <summary>
    /// Reloads the player catalog from the configured provider (with mock fallback).
    /// </summary>
    void Refresh();
}
