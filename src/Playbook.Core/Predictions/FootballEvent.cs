namespace Playbook.Core.Predictions;

/// <summary>
/// Upcoming/live football game context for a prediction. Not fantasy-league scoped.
/// Every Quick Pick event carries season / phase / week identity for slate filtering.
/// </summary>
public sealed class FootballEvent
{
    public required string EventId { get; init; }

    public required string HomeTeam { get; init; }

    public required string AwayTeam { get; init; }

    public required DateTimeOffset CommenceTime { get; init; }

    /// <summary>NFL season year (e.g. 2026).</summary>
    public int Season { get; init; }

    public NflSeasonPhase Phase { get; init; } = NflSeasonPhase.RegularSeason;

    /// <summary>Week number within the phase (preseason week, regular week, etc.).</summary>
    public int Week { get; init; }

    public string DisplayName => $"{AwayTeam} @ {HomeTeam}";

    /// <summary>Compact slate label, e.g. "Preseason · Week 1 · NE @ SEA · Aug 14".</summary>
    public string ContextLabel
    {
        get
        {
            var phase = Phase switch
            {
                NflSeasonPhase.Preseason => "Preseason",
                NflSeasonPhase.RegularSeason => "Regular Season",
                NflSeasonPhase.Postseason => "Postseason",
                _ => Phase.ToString()
            };
            var weekPart = Week > 0 ? $"Week {Week}" : "Week —";
            var date = CommenceTime.ToLocalTime().ToString("MMM d");
            return $"{phase} · {weekPart} · {DisplayName} · {date}";
        }
    }

    public NflWeekRef WeekRef => new(Season, Phase, Week);
}
