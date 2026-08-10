using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Projections.Services;

/// <summary>
/// Game-environment architecture placeholder — pace/totals/weather not wired yet.
/// </summary>
public sealed class UnavailableGameEnvironmentProvider : IGameEnvironmentProvider
{
    public string DisplayName => "Game Environment (unavailable)";

    public bool IsConfigured => false;

    public GameEnvironmentContext GetEnvironment(Player player, ProjectionLeagueContext leagueContext) =>
        GameEnvironmentContext.Unavailable(
            $"Game environment unavailable for {player.FullName} — pace/totals/weather not yet integrated.");
}
