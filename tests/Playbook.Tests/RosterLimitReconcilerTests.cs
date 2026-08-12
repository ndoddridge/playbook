using Playbook.Core.Leagues;

namespace Playbook.Tests;

public class RosterLimitReconcilerTests
{
    [Fact]
    public void Unknown_When_League_Has_No_Roster_Positions()
    {
        var league = MakeLeague(rosterPositions: []);
        var team = MakeTeam(playerCount: 20);

        var status = RosterLimitReconciler.Check(team, league);

        Assert.False(status.IsKnown);
        Assert.False(status.IsOverLimit);
        Assert.Null(status.Limit);
    }

    [Fact]
    public void Within_Limit_When_Counted_Players_At_Or_Below_Limit()
    {
        var league = MakeLeague(rosterPositions: Positions(15));
        var team = MakeTeam(playerCount: 15);

        var status = RosterLimitReconciler.Check(team, league);

        Assert.True(status.IsKnown);
        Assert.False(status.IsOverLimit);
        Assert.Equal(15, status.Limit);
        Assert.Equal(15, status.CountedCount);
    }

    [Fact]
    public void Taxi_Squad_Players_Are_Excluded_From_The_Counted_Roster()
    {
        var league = MakeLeague(rosterPositions: Positions(15));
        // 15 active + 4 taxi = 19 raw players, but only 15 count against the limit.
        var team = MakeTeam(playerCount: 19, taxiCount: 4);

        var status = RosterLimitReconciler.Check(team, league);

        Assert.True(status.IsKnown);
        Assert.False(status.IsOverLimit);
        Assert.Equal(15, status.CountedCount);
        Assert.Equal(4, status.TaxiExcludedCount);
    }

    [Fact]
    public void Over_Limit_After_Excluding_Taxi_Is_Surfaced_Not_Silently_Fixed()
    {
        var league = MakeLeague(rosterPositions: Positions(15));
        // 18 active (non-taxi) players against a 15-slot limit -> genuinely over.
        var team = MakeTeam(playerCount: 22, taxiCount: 4);

        var status = RosterLimitReconciler.Check(team, league);

        Assert.True(status.IsKnown);
        Assert.True(status.IsOverLimit);
        Assert.Equal(3, status.OverBy);
        Assert.Equal(18, status.CountedCount);
        Assert.Contains("does not remove players automatically", status.Message);
    }

    [Fact]
    public void Reconciliation_Never_Mutates_The_Roster()
    {
        var league = MakeLeague(rosterPositions: Positions(10));
        var team = MakeTeam(playerCount: 16);
        var originalIds = team.PlayerIds.ToList();

        RosterLimitReconciler.Check(team, league);

        Assert.Equal(originalIds, team.PlayerIds);
    }

    private static IReadOnlyList<string> Positions(int count) =>
        Enumerable.Range(0, count).Select(_ => "BN").ToList();

    private static League MakeLeague(IReadOnlyList<string> rosterPositions) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test League",
        Platform = LeaguePlatform.Sleeper,
        LeagueType = LeagueType.Dynasty,
        ScoringType = ScoringType.Ppr,
        NumberOfTeams = 12,
        CurrentWeek = 1,
        Season = 2026,
        IsActive = true,
        DataSource = LeagueDataSource.Sleeper,
        RosterPositions = rosterPositions
    };

    private static FantasyTeam MakeTeam(int playerCount, int taxiCount = 0)
    {
        var players = Enumerable.Range(0, playerCount).Select(_ => Guid.NewGuid()).ToList();
        var taxi = players.Take(taxiCount).ToList();
        return new FantasyTeam
        {
            LeagueId = Guid.NewGuid(),
            RosterId = 1,
            DisplayName = "Owner",
            PlayerIds = players,
            TaxiPlayerIds = taxi
        };
    }
}
