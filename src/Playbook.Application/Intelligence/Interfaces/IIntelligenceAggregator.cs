using Playbook.Core.Intelligence.Models;

namespace Playbook.Application.Intelligence.Interfaces;

/// <summary>
/// Merges per-article IntelligenceFacts into one PlayerIntelligenceProfile per player.
/// </summary>
public interface IIntelligenceAggregator
{
    IReadOnlyList<PlayerIntelligenceProfile> Aggregate(IReadOnlyList<IntelligenceFact> facts);
}
