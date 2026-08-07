using Playbook.Core.Intelligence.Models;

namespace Playbook.Application.Intelligence.Interfaces;

/// <summary>
/// Converts football information into structured intelligence profiles.
/// Downstream engines should prefer <see cref="PlayerIntelligenceProfile"/>.
/// </summary>
public interface IIntelligenceService
{
    IReadOnlyList<IntelligenceFact> GetAllFacts();

    IReadOnlyList<IntelligenceFact> GetTopFacts(int count = 8);

    IReadOnlyList<IntelligenceFact> GetFactsForPlayer(Guid playerId);

    PlayerIntelligenceProfile? GetPlayerProfile(Guid playerId);

    /// <summary>
    /// Highest-signal player intelligence changes for the dashboard.
    /// </summary>
    IReadOnlyList<PlayerIntelligenceProfile> GetTopProfiles(int count = 8);

    IReadOnlyList<PlayerIntelligenceProfile> GetAllProfiles();

    /// <summary>
    /// Legacy adapter over <see cref="GetPlayerProfile"/> for older callers.
    /// </summary>
    PlayerIntelligence? GetPlayerIntelligence(Guid playerId);

    void Refresh();
}
