namespace Playbook.Core.Predictions.Models;

/// <summary>
/// Builds <see cref="TeamPointsFeatures"/> from real completed games.
///
/// LEAKAGE DISCIPLINE — the single most important property of this class. Features for a game in
/// season S week W are built ONLY from games in season S with week &lt; W (plus a prior-season
/// mean). The game being predicted, and every later game, are excluded. This mirrors exactly how
/// the model was fitted and backtested, so runtime behaviour matches the measured error.
/// </summary>
public static class TeamPointsFeatureBuilder
{
    /// <summary>Rolling window used when the model was fitted.</summary>
    public const int RollingWindow = 6;

    public static TeamPointsFeatures? Build(
        string team,
        string opponent,
        bool isHome,
        int season,
        int week,
        IReadOnlyList<HistoricalGameScore> completedGames)
    {
        ArgumentNullException.ThrowIfNull(completedGames);

        if (string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(opponent))
        {
            return null;
        }

        // Strictly prior games in the same season.
        var priorThisSeason = completedGames
            .Where(g => g.Season == season && g.Week < week)
            .OrderBy(g => g.Week)
            .ToList();

        var teamGames = priorThisSeason.Where(g => g.Involves(team)).ToList();
        var opponentGames = priorThisSeason.Where(g => g.Involves(opponent)).ToList();

        if (teamGames.Count == 0 || opponentGames.Count == 0)
        {
            return null;
        }

        var rollingPointsFor = Mean(teamGames
            .TakeLast(RollingWindow)
            .Select(g => (decimal)g.PointsFor(team)));

        var opponentPointsAllowed = Mean(opponentGames
            .TakeLast(RollingWindow)
            .Select(g => (decimal)g.PointsAgainst(opponent)));

        // Prior-season carry-over. Falls back to this season's rolling mean when the previous
        // season is not in the dataset — an honest substitution of like for like, never a
        // league-average stand-in dressed up as team history.
        var priorSeasonGames = completedGames
            .Where(g => g.Season == season - 1 && g.Involves(team))
            .ToList();

        var priorSeasonPointsFor = priorSeasonGames.Count > 0
            ? Mean(priorSeasonGames.Select(g => (decimal)g.PointsFor(team)))
            : rollingPointsFor;

        return new TeamPointsFeatures
        {
            RollingPointsFor = rollingPointsFor,
            OpponentRollingPointsAllowed = opponentPointsAllowed,
            IsHome = isHome,
            PriorSeasonPointsFor = priorSeasonPointsFor,
            GamesObservedTeam = teamGames.Count,
            GamesObservedOpponent = opponentGames.Count
        };
    }

    private static decimal Mean(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0m : Math.Round(list.Sum() / list.Count, 2, MidpointRounding.AwayFromZero);
    }
}
