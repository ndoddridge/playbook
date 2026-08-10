using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Players.Data;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;

namespace Playbook.Tests;

public class WeeklyMatchupGamePlanTests
{
    [Fact]
    public void Plan_Loads_For_Current_Team_With_Opponent()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var gamePlan = provider.GetRequiredService<IWeeklyMatchupGamePlanService>();

        var mine = leagues.GetCurrentUserTeam();
        Assert.NotNull(mine);

        var plan = gamePlan.GetPlan();
        Assert.True(plan.HasMatchup);
        Assert.Equal(leagues.CurrentLeague!.Id, plan.LeagueId);
        Assert.Equal(mine!.RosterId, plan.SelectedRosterId);
        Assert.NotNull(plan.OpponentRosterId);
        Assert.NotEqual(mine.RosterId, plan.OpponentRosterId);
        Assert.Equal(leagues.CurrentLeague.CurrentWeek, plan.Week);
        Assert.False(string.IsNullOrWhiteSpace(plan.AssessmentLabel));
        Assert.InRange(plan.MatchupConfidence, 1, 100);
    }

    [Fact]
    public void Switching_Owned_Team_Rebuilds_Entire_Plan()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var gamePlan = provider.GetRequiredService<IWeeklyMatchupGamePlanService>();

        var before = gamePlan.GetPlan();
        Assert.Equal(1, before.SelectedRosterId);

        Assert.True(leagues.SelectUserTeam(leagues.CurrentLeague!.Id, 2));
        var after = gamePlan.GetPlan();

        Assert.Equal(2, after.SelectedRosterId);
        Assert.NotEqual(before.MyTeamName, after.MyTeamName);
        Assert.NotEqual(after.SelectedRosterId, after.OpponentRosterId);
        Assert.NotEqual(before.OpponentRosterId + ":" + before.SelectedRosterId,
                        after.OpponentRosterId + ":" + after.SelectedRosterId);
    }

    [Fact]
    public void Switching_League_Updates_Matchup_Context()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var gamePlan = provider.GetRequiredService<IWeeklyMatchupGamePlanService>();

        var friends = leagues.GetAllLeagues().Single(l => l.Name == "Friends League");
        var dynasty = leagues.GetAllLeagues().Single(l => l.Name == "Dynasty League");

        leagues.SelectLeague(friends.Id);
        var friendsPlan = gamePlan.GetPlan();
        Assert.Equal("PPR", friendsPlan.ScoringLabel);

        leagues.SelectLeague(dynasty.Id);
        var dynastyPlan = gamePlan.GetPlan();

        Assert.Equal(dynasty.Id, dynastyPlan.LeagueId);
        Assert.Equal("Half PPR", dynastyPlan.ScoringLabel);
        Assert.NotEqual(friendsPlan.LeagueName, dynastyPlan.LeagueName);
    }

    [Fact]
    public void Lineup_Impact_Connects_StartSit_To_Matchup()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var gamePlan = provider.GetRequiredService<IWeeklyMatchupGamePlanService>();
        var plan = gamePlan.GetPlan();

        Assert.True(plan.HasMatchup);
        Assert.All(plan.LineupImpact, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.MatchupRelevance));
            Assert.False(string.IsNullOrWhiteSpace(item.IfStarted));
            Assert.False(string.IsNullOrWhiteSpace(item.IfSat));
            Assert.True(item.Reasons.Count <= 3);
        });
    }

    [Fact]
    public void Incomplete_Coverage_Does_Not_Fabricate_High_Certainty()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var gamePlan = provider.GetRequiredService<IWeeklyMatchupGamePlanService>();
        var plan = gamePlan.GetPlan();

        Assert.Contains(
            plan.UnavailableSignals,
            s => s.Contains("head-to-head", StringComparison.OrdinalIgnoreCase) ||
                 s.Contains("Derived", StringComparison.OrdinalIgnoreCase) ||
                 s.Contains("unavailable", StringComparison.OrdinalIgnoreCase));

        if (plan.MatchupConfidence < 45)
        {
            Assert.Contains("low", plan.ConfidenceNote, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Opponent_Scout_Uses_Actual_Opponent_Roster()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var gamePlan = provider.GetRequiredService<IWeeklyMatchupGamePlanService>();

        var plan = gamePlan.GetPlan();
        var opponent = leagues.GetCurrentTeams().Single(t => t.RosterId == plan.OpponentRosterId);
        Assert.Equal(
            string.IsNullOrWhiteSpace(opponent.TeamName) ? opponent.DisplayName : opponent.TeamName,
            plan.OpponentScout.TeamName);
        Assert.All(
            plan.OpponentScout.SwingPlayers,
            p => Assert.Contains(p.PlayerId, opponent.PlayerIds));
    }
}
