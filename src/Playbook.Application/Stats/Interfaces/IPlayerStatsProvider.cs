using Playbook.Core.Stats.Models;

namespace Playbook.Application.Stats.Interfaces;

/// <summary>
/// Source of normalized player statistics (Mock or Live). UI never consumes this directly.
/// </summary>
public interface IPlayerStatsProvider
{
    PlayerStatsProviderKind Kind { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<PlayerSeasonStats>> GetSeasonStatsAsync(
        PlayerStatsSyncRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PlayerStatsSyncRequest
{
    public required int CurrentSeason { get; init; }

    public required IReadOnlyList<int> CompletedSeasons { get; init; }

    public required string SeasonType { get; init; }
}
