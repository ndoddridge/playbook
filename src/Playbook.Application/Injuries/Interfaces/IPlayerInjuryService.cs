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

    /// <summary>
    /// Loads from the saved cache when it is still fresh, otherwise from the provider. The browser
    /// build calls this at startup so a repeat visit does not re-download what it already has.
    /// </summary>
    Task EnsureLoadedAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);
}
