using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Application.Projections.Interfaces;

/// <summary>
/// Optional game-environment data. Implementations may return unavailable — never fabricate.
/// </summary>
public interface IGameEnvironmentProvider
{
    string DisplayName { get; }

    bool IsConfigured { get; }

    GameEnvironmentContext GetEnvironment(Player player, ProjectionLeagueContext leagueContext);
}
