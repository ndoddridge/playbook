namespace Playbook.Core.Draft;

/// <summary>
/// Which regular-season week each team is off, derived from the real published schedule.
///
/// NOT INVENTED. A team's bye is simply the regular-season week in which it appears in no
/// scheduled game. That is a fact about the published fixture list, not an estimate — which is
/// why this is allowed to influence recommendations while other schedule-derived factors
/// (strength of schedule, playoff difficulty) are not: those need opponent QUALITY, which
/// Playbook cannot currently measure.
/// </summary>
public sealed class ByeWeekMap
{
    private readonly IReadOnlyDictionary<string, int> _byeByTeam;

    private ByeWeekMap(IReadOnlyDictionary<string, int> byeByTeam)
    {
        _byeByTeam = byeByTeam;
    }

    public static ByeWeekMap Empty { get; } =
        new(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

    public bool IsAvailable => _byeByTeam.Count > 0;

    public int TeamsCovered => _byeByTeam.Count;

    public int? ByeWeekFor(string? team) =>
        !string.IsNullOrWhiteSpace(team) && _byeByTeam.TryGetValue(team, out var week) ? week : null;

    /// <summary>
    /// Build from scheduled games. A team's bye is the week it does not appear.
    ///
    /// Returns <see cref="Empty"/> unless the schedule looks complete enough to trust: a partial
    /// fixture list would make almost every week look like a bye, so a team is only credited with
    /// a bye when it is missing from exactly one week of an otherwise full slate.
    /// </summary>
    public static ByeWeekMap Build(IEnumerable<ScheduledGame> schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var games = schedule.ToList();
        if (games.Count == 0)
        {
            return Empty;
        }

        var weeks = games.Select(g => g.Week).Distinct().OrderBy(w => w).ToList();

        // A real NFL regular season runs 18 weeks. Anything materially short of that is a
        // partial download, and inferring byes from it would be fabrication.
        if (weeks.Count < 17)
        {
            return Empty;
        }

        var allWeeks = weeks.ToHashSet();
        var weeksByTeam = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var game in games)
        {
            Add(weeksByTeam, game.HomeTeam, game.Week);
            Add(weeksByTeam, game.AwayTeam, game.Week);
        }

        var byes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (team, played) in weeksByTeam)
        {
            var missing = allWeeks.Except(played).ToList();

            // Exactly one missing week is a bye. Zero or several means the data is incomplete
            // for that team, so no claim is made.
            if (missing.Count == 1)
            {
                byes[team] = missing[0];
            }
        }

        return byes.Count == 0 ? Empty : new ByeWeekMap(byes);
    }

    private static void Add(Dictionary<string, HashSet<int>> map, string team, int week)
    {
        if (string.IsNullOrWhiteSpace(team))
        {
            return;
        }

        if (!map.TryGetValue(team, out var set))
        {
            set = [];
            map[team] = set;
        }

        set.Add(week);
    }
}

/// <summary>One scheduled regular-season game. Scores are irrelevant here.</summary>
public sealed record ScheduledGame
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required string HomeTeam { get; init; }

    public required string AwayTeam { get; init; }
}

/// <summary>
/// Penalty for stacking too many same-position starters on one bye week.
/// </summary>
public static class ByeWeekCollisionPolicy
{
    /// <summary>Applied per already-drafted same-position player sharing this bye, beyond the first.</summary>
    public const decimal PenaltyPerCollision = -1.2m;

    /// <summary>Bounded so a bye clash can shade a close call but never dominate the board.</summary>
    public const decimal MaxPenalty = -3.6m;

    /// <summary>
    /// Zero when the bye is unknown, when nothing else shares it, or when only one other
    /// same-position player does — one overlap is normal roster construction, not a problem.
    /// </summary>
    public static decimal Penalty(int samePositionPlayersOnSameBye)
    {
        if (samePositionPlayersOnSameBye <= 1)
        {
            return 0m;
        }

        return Math.Max(MaxPenalty, (samePositionPlayersOnSameBye - 1) * PenaltyPerCollision);
    }
}
