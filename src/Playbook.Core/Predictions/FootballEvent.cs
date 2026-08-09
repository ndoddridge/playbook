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

    /// <summary>
    /// Week number within the phase.
    /// Preseason 1–3, regular 1–18, postseason 1–4 (Wild Card → Super Bowl).
    /// </summary>
    public int Week { get; init; }

    /// <summary>
    /// Optional phase hint from the prop provider sport key
    /// (e.g. americanfootball_nfl_preseason → Preseason). Used during enrichment.
    /// </summary>
    public NflSeasonPhase? PhaseHint { get; init; }

    public string DisplayName => $"{AwayTeam} @ {HomeTeam}";

    public string MatchupKey =>
        string.Join(" ", new[] { AwayTeam, HomeTeam, $"{AwayTeam}@{HomeTeam}", $"{AwayTeam} vs {HomeTeam}" });

    public string WeekLabel => new NflWeekRef(Season, Phase, Week).WeekLabel;

    public string PhaseLabel => new NflWeekRef(Season, Phase, Week).PhaseLabel;

    /// <summary>Slate identity line, e.g. "Preseason · Week 1 · Aug 14".</summary>
    public string SlateLabel
    {
        get
        {
            var slate = new NflWeekRef(Season, Phase, Week).DisplayLabel;
            var date = CommenceTime.ToLocalTime().ToString("MMM d");
            return $"{slate} · {date}";
        }
    }

    /// <summary>Full context, e.g. "Preseason · Week 1 · DET @ CIN · Aug 14".</summary>
    public string ContextLabel => $"{new NflWeekRef(Season, Phase, Week).DisplayLabel} · {DisplayName} · {CommenceTime.ToLocalTime():MMM d}";

    public NflWeekRef WeekRef => new(Season, Phase, Week);
}
