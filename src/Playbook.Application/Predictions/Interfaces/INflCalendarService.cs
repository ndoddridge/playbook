using Playbook.Core.Predictions;

namespace Playbook.Application.Predictions.Interfaces;

/// <summary>
/// Resolves NFL season context and builds available slates from real game/event dates.
/// </summary>
public interface INflCalendarService
{
    /// <summary>Live NFL calendar snapshot (season/phase hint from provider, with fallback).</summary>
    NflSeasonContext GetCurrentContext();

    /// <summary>
    /// Assign season/phase/week on each event from kickoff clustering + provider phase hints.
    /// Never invents weeks without games. Preseason capped at 3 weeks.
    /// </summary>
    IReadOnlyList<FootballEvent> EnrichEvents(
        IReadOnlyList<FootballEvent> events,
        NflSeasonContext current);

    /// <summary>Build concrete slates that have at least one real event.</summary>
    IReadOnlyList<NflSlate> BuildSlates(IReadOnlyList<FootballEvent> enrichedEvents);

    /// <summary>Distinct slate refs present in an enriched event list.</summary>
    IReadOnlyList<NflWeekRef> GetAvailableWeeks(IReadOnlyList<FootballEvent> events);

    /// <summary>
    /// Default slate: next incomplete available slate (by event dates).
    /// Preferred selection wins when still available.
    /// </summary>
    NflWeekRef SelectActiveWeek(
        IReadOnlyList<NflSlate> available,
        NflSeasonContext current,
        NflWeekRef? preferred = null,
        DateTimeOffset? utcNow = null);
}
