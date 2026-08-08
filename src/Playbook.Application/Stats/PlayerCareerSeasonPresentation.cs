using Playbook.Core.Stats.Models;

namespace Playbook.Application.Stats;

/// <summary>
/// Career / College season selector rules shared by UI and tests.
/// </summary>
public static class PlayerCareerSeasonPresentation
{
    public const int DefaultCollegePromotionYearsProThreshold = 3;

    public static IReadOnlyList<PlayerSeasonStats> ForCareerSelector(
        IEnumerable<PlayerSeasonStats> all,
        int? yearsPro,
        int promotionThreshold = DefaultCollegePromotionYearsProThreshold)
    {
        var rows = all
            .OrderByDescending(r => r.Season)
            .ThenBy(r => r.Period)
            .ToList();

        var promoteCollege = (yearsPro ?? 0) < promotionThreshold;
        if (promoteCollege)
        {
            return rows;
        }

        return rows.Where(r => r.Period != StatsPeriod.College).ToList();
    }

    public static IReadOnlyList<PlayerSeasonStats> ForCollegeTab(IEnumerable<PlayerSeasonStats> all) =>
        all
            .Where(r => r.Period == StatsPeriod.College)
            .OrderByDescending(r => r.Season)
            .ToList();

    public static string FormatPeriodLabel(StatsPeriod period) => period switch
    {
        StatsPeriod.CurrentSeason => "Current Season (NFL)",
        StatsPeriod.CompletedSeason => "Completed Season (NFL)",
        StatsPeriod.College => "College Season",
        _ => period.ToString()
    };

    public static string FormatSeasonOption(PlayerSeasonStats row)
    {
        var label = $"{row.Season} · {FormatPeriodLabel(row.Period)}";
        if (!string.IsNullOrWhiteSpace(row.CollegeSchool))
        {
            label += $" · {row.CollegeSchool}";
        }

        return label;
    }

    public static string StatsKey(PlayerSeasonStats row) =>
        $"{row.Season}:{row.Period}:{row.SeasonType}";
}
