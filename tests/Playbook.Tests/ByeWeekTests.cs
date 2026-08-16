using Playbook.Core.Draft;
using Playbook.Infrastructure.Draft;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// Bye weeks are derived from the real published schedule — a team's bye is simply the week it
/// plays nobody. These tests pin that derivation and, more importantly, pin the refusal to guess
/// when the schedule is incomplete.
/// </summary>
public class ByeWeekTests
{
    [Fact]
    public void Bye_IsTheWeekATeamDoesNotAppear()
    {
        var map = ByeWeekMap.Build(FullSeason(byeWeekForA: 7));

        Assert.True(map.IsAvailable);
        Assert.Equal(7, map.ByeWeekFor("AAA"));
    }

    [Fact]
    public void PartialSchedule_ProducesNoByes_RatherThanNonsense()
    {
        // Only 5 weeks downloaded. Every team is "missing" from 13 weeks; inferring byes here
        // would invent 13 byes per team.
        var partial = FullSeason(byeWeekForA: 7).Where(g => g.Week <= 5).ToList();

        var map = ByeWeekMap.Build(partial);

        Assert.False(map.IsAvailable);
        Assert.Null(map.ByeWeekFor("AAA"));
    }

    [Fact]
    public void EmptySchedule_IsUnavailable()
    {
        Assert.False(ByeWeekMap.Build([]).IsAvailable);
        Assert.False(ByeWeekMap.Empty.IsAvailable);
    }

    [Fact]
    public void UnknownTeam_ReturnsNull_NotAGuess()
    {
        var map = ByeWeekMap.Build(FullSeason(byeWeekForA: 7));

        Assert.Null(map.ByeWeekFor("ZZZ"));
        Assert.Null(map.ByeWeekFor(null));
    }

    // ---------------------------------------------------------------- collision policy

    [Fact]
    public void SingleStarterOnABye_IsNotPenalised()
    {
        // One player on a bye is normal roster construction, not a problem to solve.
        Assert.Equal(0m, ByeWeekCollisionPolicy.Penalty(1));
        Assert.Equal(0m, ByeWeekCollisionPolicy.Penalty(0));
    }

    [Fact]
    public void StackingSamePositionOnOneBye_IsPenalised_AndBounded()
    {
        var two = ByeWeekCollisionPolicy.Penalty(2);
        var three = ByeWeekCollisionPolicy.Penalty(3);
        var absurd = ByeWeekCollisionPolicy.Penalty(12);

        Assert.True(two < 0m);
        Assert.True(three < two, "more collisions should hurt more");
        Assert.True(absurd >= ByeWeekCollisionPolicy.MaxPenalty, "penalty must stay bounded");
    }

    // ---------------------------------------------------------------- real parser

    [Fact]
    public void Parser_ReadsScheduledGames_IncludingUnplayedOnes()
    {
        // 2026 games have empty score columns. They must still be read — byes are known before
        // a season starts, which is exactly when a draft happens.
        var csv = new[]
        {
            "game_id,season,game_type,week,gameday,away_team,away_score,home_team,home_score",
            "2026_01_NE_SEA,2026,REG,1,2026-09-09,NE,,SEA,",
            "2026_02_NE_BUF,2026,REG,2,2026-09-16,NE,,BUF,",
            "2025_01_NE_SEA,2025,REG,1,2025-09-09,NE,20,SEA,27",
            "2026_pre,2026,PRE,1,2026-08-10,NE,,SEA,"
        };

        var games = NflverseByeWeekProvider.ParseSchedule(csv, 2026);

        Assert.Equal(2, games.Count);                       // other season and PRE excluded
        Assert.All(games, g => Assert.Equal(2026, g.Season));
        Assert.Contains(games, g => g.Week == 1 && g.HomeTeam == "SEA");
    }

    [Fact]
    public void Parser_ExcludesOtherSeasons()
    {
        var csv = new[]
        {
            "game_id,season,game_type,week,gameday,away_team,away_score,home_team,home_score",
            "2025_01_NE_SEA,2025,REG,1,2025-09-09,NE,20,SEA,27"
        };

        Assert.Empty(NflverseByeWeekProvider.ParseSchedule(csv, 2026));
    }

    /// <summary>
    /// An 18-week season for four teams. AAA sits out <paramref name="byeWeekForA"/>; the others
    /// are paired so the fixture stays internally consistent.
    /// </summary>
    private static List<ScheduledGame> FullSeason(int byeWeekForA)
    {
        var games = new List<ScheduledGame>();

        for (var week = 1; week <= 18; week++)
        {
            if (week != byeWeekForA)
            {
                games.Add(Game(week, "AAA", "BBB"));
            }
            else
            {
                games.Add(Game(week, "BBB", "CCC"));
            }

            games.Add(Game(week, "CCC", "DDD"));
        }

        return games;
    }

    private static ScheduledGame Game(int week, string home, string away) => new()
    {
        Season = 2026,
        Week = week,
        HomeTeam = home,
        AwayTeam = away
    };
}
