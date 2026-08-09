using Playbook.Core.Predictions;

namespace Playbook.Application.Predictions.Interfaces;

/// <summary>
/// Resolves NFL season/phase/week context for Quick Picks (league-independent).
/// </summary>
public interface INflCalendarService
{
    /// <summary>Current NFL season context from the live state provider (with calendar fallback).</summary>
    NflSeasonContext GetCurrentContext();

    /// <summary>
    /// Assign season/phase/week on each event from kickoff + current NFL state.
    /// Never invents a different season phase than the resolved state when games are current-slate.
    /// </summary>
    IReadOnlyList<FootballEvent> EnrichEvents(
        IReadOnlyList<FootballEvent> events,
        NflSeasonContext current);

    /// <summary>Distinct weeks present in an enriched event list, ordered by season/phase/week.</summary>
    IReadOnlyList<NflWeekRef> GetAvailableWeeks(IReadOnlyList<FootballEvent> events);

    /// <summary>
    /// Choose the active slate week: prefer <paramref name="preferred"/> when present in available,
    /// else current week when present, else the soonest upcoming available week.
    /// </summary>
    NflWeekRef SelectActiveWeek(
        IReadOnlyList<NflWeekRef> available,
        NflSeasonContext current,
        NflWeekRef? preferred = null);
}
