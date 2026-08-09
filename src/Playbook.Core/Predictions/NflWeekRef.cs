namespace Playbook.Core.Predictions;

/// <summary>
/// Stable NFL slate identity (season + phase + week/round).
/// Canonical order: Preseason 1–3 → Regular 1–18 → Wild Card → Divisional → Conference Championship → Super Bowl.
/// </summary>
public sealed record NflWeekRef(int Season, NflSeasonPhase Phase, int Week) : IComparable<NflWeekRef>
{
    public const int MaxPreseasonWeeks = 3;
    public const int MaxRegularSeasonWeeks = 18;
    public const int MaxPostseasonRounds = 4;

    public static IComparer<NflWeekRef> CanonicalComparer { get; } = Comparer<NflWeekRef>.Create(CompareCanonical);

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
            3 => "Conference Championship",
            4 => "Super Bowl",
            _ => $"Round {Week}"
        },
        _ => $"Week {Week}"
    };

    /// <summary>Full slate label, e.g. "Preseason · Week 1" or "Postseason · Wild Card".</summary>
    public string DisplayLabel => $"{PhaseLabel} · {WeekLabel}";

    /// <summary>Compact navigator center label, e.g. "PRESEASON · WEEK 1".</summary>
    public string CompactLabel => DisplayLabel.ToUpperInvariant();

    /// <summary>Compact chip/picker label (W1, WC, …).</summary>
    public string ShortLabel => Phase switch
    {
        NflSeasonPhase.Preseason => $"W{Week}",
        NflSeasonPhase.RegularSeason => $"W{Week}",
        NflSeasonPhase.Postseason => Week switch
        {
            1 => "WC",
            2 => "DIV",
            3 => "CONF",
            4 => "SB",
            _ => $"R{Week}"
        },
        _ => Week.ToString()
    };

    /// <summary>Phase sort key for canonical NFL order.</summary>
    public int PhaseOrder => Phase switch
    {
        NflSeasonPhase.Preseason => 0,
        NflSeasonPhase.RegularSeason => 1,
        NflSeasonPhase.Postseason => 2,
        _ => 9
    };

    /// <summary>Linear index within a season for prev/next navigation (0-based).</summary>
    public int CanonicalIndex => Phase switch
    {
        NflSeasonPhase.Preseason => Week - 1,
        NflSeasonPhase.RegularSeason => MaxPreseasonWeeks + Week - 1,
        NflSeasonPhase.Postseason => MaxPreseasonWeeks + MaxRegularSeasonWeeks + Week - 1,
        _ => 0
    };

    public bool Matches(FootballEvent ev) =>
        ev.Season == Season && ev.Phase == Phase && ev.Week == Week;

    public int CompareTo(NflWeekRef? other) =>
        other is null ? 1 : CompareCanonical(this, other);

    public static int CompareCanonical(NflWeekRef? a, NflWeekRef? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a is null)
        {
            return -1;
        }

        if (b is null)
        {
            return 1;
        }

        var bySeason = a.Season.CompareTo(b.Season);
        if (bySeason != 0)
        {
            return bySeason;
        }

        var byPhase = a.PhaseOrder.CompareTo(b.PhaseOrder);
        if (byPhase != 0)
        {
            return byPhase;
        }

        return a.Week.CompareTo(b.Week);
    }

    public static int MaxWeekForPhase(NflSeasonPhase phase) => phase switch
    {
        NflSeasonPhase.Preseason => MaxPreseasonWeeks,
        NflSeasonPhase.RegularSeason => MaxRegularSeasonWeeks,
        NflSeasonPhase.Postseason => MaxPostseasonRounds,
        _ => MaxRegularSeasonWeeks
    };

    /// <summary>
    /// Full canonical season structure — never invents games, only week/round identities.
    /// Preseason is exactly W1–W3; no Week 4+.
    /// </summary>
    public static IReadOnlyList<NflWeekRef> BuildCanonicalSeason(int season)
    {
        var weeks = new List<NflWeekRef>(MaxPreseasonWeeks + MaxRegularSeasonWeeks + MaxPostseasonRounds);
        for (var w = 1; w <= MaxPreseasonWeeks; w++)
        {
            weeks.Add(new NflWeekRef(season, NflSeasonPhase.Preseason, w));
        }

        for (var w = 1; w <= MaxRegularSeasonWeeks; w++)
        {
            weeks.Add(new NflWeekRef(season, NflSeasonPhase.RegularSeason, w));
        }

        for (var w = 1; w <= MaxPostseasonRounds; w++)
        {
            weeks.Add(new NflWeekRef(season, NflSeasonPhase.Postseason, w));
        }

        return weeks;
    }

    public NflWeekRef? PreviousInSeason()
    {
        var all = BuildCanonicalSeason(Season);
        var idx = all.ToList().FindIndex(w => w == this);
        return idx > 0 ? all[idx - 1] : null;
    }

    public NflWeekRef? NextInSeason()
    {
        var all = BuildCanonicalSeason(Season);
        var idx = all.ToList().FindIndex(w => w == this);
        return idx >= 0 && idx < all.Count - 1 ? all[idx + 1] : null;
    }
}
