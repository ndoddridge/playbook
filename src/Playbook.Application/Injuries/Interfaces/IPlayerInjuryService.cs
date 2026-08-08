using Playbook.Core.Injuries.Models;

namespace Playbook.Application.Injuries.Interfaces;

/// <summary>
/// Application facade for player injuries. Distinguishes current vs historical availability.
/// </summary>
public interface IPlayerInjuryService
{
    InjuryProviderCapabilities ActiveCapabilities { get; }

    HistoricalDataStatus GlobalHistoricalDataStatus { get; }

    IReadOnlyList<PlayerInjuryRecord> GetAllInjuries();

    IReadOnlyList<PlayerInjuryRecord> GetInjuriesForPlayer(Guid playerId);

    PlayerInjuryRecord? GetCurrentInjury(Guid playerId);

    IReadOnlyList<PlayerInjuryRecord> GetHistoricalInjuries(Guid playerId);

    PlayerInjuryProfile GetPlayerInjuryProfile(Guid playerId);

    void Refresh();

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
