using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Application.Projections.Interfaces;

/// <summary>
/// Resolves player-specific production for Projection baselines.
/// Future live stats providers should implement this without changing ProjectionEngine.
/// </summary>
public interface IPlayerProductionProvider
{
    PlayerProductionSnapshot GetProduction(Player player);
}
