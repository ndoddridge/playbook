using Playbook.Core.Intelligence.Models;

namespace Playbook.Application.Intelligence.Interfaces;

/// <summary>
/// Converts football information into structured intelligence.
/// Does not produce predictions, recommendations, or fantasy valuations.
/// </summary>
public interface IIntelligenceService
{
    IReadOnlyList<IntelligenceFact> GetAllFacts();

    /// <summary>
    /// Highest-priority facts Playbook currently believes matter most.
    /// </summary>
    IReadOnlyList<IntelligenceFact> GetTopFacts(int count = 8);

    PlayerIntelligence? GetPlayerIntelligence(Guid playerId);

    IReadOnlyList<IntelligenceFact> GetFactsForPlayer(Guid playerId);

    /// <summary>
    /// Re-runs analysis against the current news + player catalogs.
    /// </summary>
    void Refresh();
}
