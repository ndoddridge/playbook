using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Internal assessment of which historical domains can support trustworthy multi-year replay.
/// Statuses: Available / Partial / Unavailable. Do not invent missing domains.
/// </summary>
public static class HistoricalDataAvailabilityAssessment
{
    public static IReadOnlyList<HistoricalDataAvailabilityItem> Current { get; } =
    [
        new()
        {
            Domain = "Player game statistics (nflverse)",
            Status = HistoricalDataAvailability.Partial,
            Notes = "Season CSV archives exist via NflversePlayerStatsProvider; depth capped below 20 years in sync options; as-of week filtering not yet wired into live stats services."
        },
        new()
        {
            Domain = "Fantasy scoring from counting stats",
            Status = HistoricalDataAvailability.Available,
            Notes = "LeagueFantasyScoring can recompute Standard/HalfPPR/PPR from canonical counting stats. Custom platform scoring matrices are not modeled."
        },
        new()
        {
            Domain = "Historical fantasy rosters / ownership",
            Status = HistoricalDataAvailability.Unavailable,
            Notes = "League/FantasyTeam models are live Sleeper or mock demos. No week-by-week historical ownership archive."
        },
        new()
        {
            Domain = "Injuries (as-of)",
            Status = HistoricalDataAvailability.Partial,
            Notes = "Nflverse historical injury provider exists with multi-season files, but live injury services are current-state oriented; cutoff enforcement is not applied outside Replay."
        },
        new()
        {
            Domain = "News archive",
            Status = HistoricalDataAvailability.Unavailable,
            Notes = "NewsProvider uses live ESPN feeds. No historical news warehouse for as-of reconstruction."
        },
        new()
        {
            Domain = "Depth charts",
            Status = HistoricalDataAvailability.Unavailable,
            Notes = "No historical depth-chart source is integrated."
        },
        new()
        {
            Domain = "Snap / usage shares",
            Status = HistoricalDataAvailability.Partial,
            Notes = "Some usage proxies may appear in nflverse/stats contexts for recent seasons; not reconstructed as cutoff-safe signals for arbitrary historical weeks."
        },
        new()
        {
            Domain = "Projections (as-of)",
            Status = HistoricalDataAvailability.Unavailable,
            Notes = "ProjectionEngine is live/versioned (0.1) but does not store historical pre-week projection archives. Replay v1 uses fixture projections only."
        },
        new()
        {
            Domain = "Betting / matchup context",
            Status = HistoricalDataAvailability.Unavailable,
            Notes = "Odds/prop lines and matchup environment providers are live (or unavailable stubs). Not reconstructable for historical weeks."
        },
        new()
        {
            Domain = "Player availability / active roster universe",
            Status = HistoricalDataAvailability.Partial,
            Notes = "Current catalog is live Sleeper players. Historical active universes for retired players are not retained as as-of snapshots."
        },
        new()
        {
            Domain = "Decision records / outcomes",
            Status = HistoricalDataAvailability.Partial,
            Notes = "DecisionRecord shape supports ActualOutcome/EvaluationResult; store is in-memory only (not durable across process restarts)."
        },
        new()
        {
            Domain = "NFL calendar / week identity",
            Status = HistoricalDataAvailability.Partial,
            Notes = "NflWeekRef can name canonical weeks; NflCalendarService resolves live state, not an archived historical schedule API."
        }
    ];
}
