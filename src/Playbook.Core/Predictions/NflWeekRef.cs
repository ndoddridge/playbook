namespace Playbook.Core.Predictions;

/// <summary>
/// Stable NFL slate identity (season + phase + week/round).
/// Preseason weeks: 1–3. Regular: 1–18. Postseason: 1–4 (Wild Card → Super Bowl).
/// </summary>
public sealed record NflWeekRef(int Season, NflSeasonPhase Phase, int Week)
{
    public const int MaxPreseasonWeeks = 3;
    public const int MaxRegularSeasonWeeks = 18;
    public const int MaxPostseasonRounds = 4;

    public string PhaseLabel => Phase switch
    {
        NflSeasonPhase.Preseason => "Preseason",
        NflSeasonPhase.RegularSeason => "Regular Season",
        NflSeasonPhase.Postseason => "Postseason",
        _ => Phase.ToString()
    };

    /// <summary>Week or postseason round label (e.g. "Week 1", "Wild Card").</summary>
    public string WeekLabel => Phase switch
    {
        NflSeasonPhase.Postseason => Week switch
        {
            1 => "Wild Card",
            2 => "Divisional",
            3 => "Conference",
            4 => "Super Bowl",
            _ => $"Round {Week}"
        },
        _ => $"Week {Week}"
    };

    /// <summary>Full slate label, e.g. "Preseason · Week 1" or "Postseason · Wild Card".</summary>
    public string DisplayLabel => $"{PhaseLabel} · {WeekLabel}";

    /// <summary>Compact chip label for the navigator.</summary>
    public string ShortLabel => Phase switch
    {
        NflSeasonPhase.Preseason => $"Week {Week}",
        NflSeasonPhase.RegularSeason => $"W{Week}",
        NflSeasonPhase.Postseason => Week switch
        {
            1 => "Wild Card",
            2 => "Divisional",
            3 => "Conference",
            4 => "Super Bowl",
            _ => $"R{Week}"
        },
        _ => Week.ToString()
    };

    public bool Matches(FootballEvent ev) =>
        ev.Season == Season && ev.Phase == Phase && ev.Week == Week;
}
