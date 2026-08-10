namespace Playbook.Core.Injuries.Models;

/// <summary>Historical injury row plus transparent relevance to current evaluation.</summary>
public sealed class InjuryHistoryEntry
{
    public required PlayerInjuryRecord Record { get; init; }

    /// <summary>0–100 relevance to the player's current outlook.</summary>
    public int RelevanceScore { get; init; }

    public InjuryRelevanceBand Band { get; init; }

    public string? RelevanceReason { get; init; }

    public string EmphasisCssClass => Band switch
    {
        InjuryRelevanceBand.High => "player-injury-entry--high",
        InjuryRelevanceBand.Moderate => "player-injury-entry--moderate",
        InjuryRelevanceBand.Low => "player-injury-entry--low",
        _ => "player-injury-entry--minimal"
    };
}
