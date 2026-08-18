using Playbook.Core.Historical;

namespace Playbook.Core.Draft;

/// <summary>Read-only historical timing context for a player the user explicitly marked as a target.</summary>
public sealed record HistoricalTargetWatch(
    Guid PlayerId, string PlayerName, string Position, HistoricalTargetTimingAssessment Assessment,
    int CurrentPick, int? NextPick, int? PicksUntilNextTurn);
