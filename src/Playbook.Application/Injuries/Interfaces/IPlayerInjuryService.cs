using Playbook.Core.Injuries.Models;

namespace Playbook.Application.Injuries.Interfaces;

/// <summary>
/// Application facade for player injuries. Preserves historical records across syncs.
/// </summary>
public interface IPlayerInjuryService
{
    IReadOnlyList<PlayerInjuryRecord> GetAllInjuries();

    IReadOnlyList<PlayerInjuryRecord> GetInjuriesForPlayer(Guid playerId);

    PlayerInjuryRecord? GetCurrentInjury(Guid playerId);

    IReadOnlyList<PlayerInjuryRecord> GetHistoricalInjuries(Guid playerId);

    void Refresh();

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
