using Playbook.Core.Predictions;

namespace Playbook.Application.Predictions;

/// <summary>
/// Live NFL calendar snapshot used to tag games and select the active Quick Picks slate.
/// </summary>
public sealed class NflSeasonContext
{
    public required int Season { get; init; }

    public required NflSeasonPhase Phase { get; init; }

    /// <summary>Provider week number for the active phase (pre / regular / post).</summary>
    public required int Week { get; init; }

    /// <summary>Preferred UI week when the provider distinguishes display vs scoring week.</summary>
    public int DisplayWeek { get; init; }

    /// <summary>
    /// Phase-relative start date from the NFL state provider (preseason start while pre;
    /// regular-season start once regular begins).
    /// </summary>
    public DateOnly? PhaseStartDate { get; init; }

    /// <summary>
    /// Regular-season kickoff week start when known. Used to separate preseason vs regular
    /// when events are mixed. Null until regular season (or inferred from Odds events).
    /// </summary>
    public DateOnly? RegularSeasonStartDate { get; init; }

    public DateTimeOffset ResolvedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? Source { get; init; }

    public NflWeekRef CurrentWeekRef
    {
        get
        {
            var week = Week > 0 ? Week : DisplayWeek;
            if (Phase == NflSeasonPhase.Preseason)
            {
                week = Math.Clamp(week, 1, NflWeekRef.MaxPreseasonWeeks);
            }

            return new NflWeekRef(Season, Phase, week);
        }
    }

    public string PhaseLabel => CurrentWeekRef.PhaseLabel;

    public string DisplayLabel => CurrentWeekRef.DisplayLabel;
}
