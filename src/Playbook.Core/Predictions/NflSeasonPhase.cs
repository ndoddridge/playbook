namespace Playbook.Core.Predictions;

/// <summary>
/// NFL season phase for Quick Picks game/week context.
/// Resolved from live NFL state — not hardcoded to a calendar date.
/// </summary>
public enum NflSeasonPhase
{
    Preseason = 0,
    RegularSeason = 1,
    Postseason = 2
}
