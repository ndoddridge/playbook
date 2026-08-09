using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Players.Data;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;

namespace Playbook.Tests;

public class FantasyTeamIntelligenceTests
{
    [Fact]
    public void Report_Uses_Current_Owned_Team_Roster()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var intel = provider.GetRequiredService<IFantasyTeamIntelligenceService>();

        var league = leagues.CurrentLeague!;
        var team = leagues.GetCurrentUserTeam();
        Assert.NotNull(team);
        Assert.NotEmpty(team!.PlayerIds);

        var report = intel.GetReport();
        Assert.Equal(league.Id, report.LeagueId);
        Assert.Equal(team.RosterId, report.SelectedRosterId);
        Assert.True(report.HasRosterPlayers);
        Assert.NotEmpty(report.RosterIntelligence);
        Assert.DoesNotContain(
            report.UnavailableSignals,
            s => s.Contains("unavailable in catalog", StringComparison.OrdinalIgnoreCase));
        Assert.All(report.RosterIntelligence, r => Assert.Contains(r.PlayerId, team.PlayerIds));
    }

    [Fact]
    public void Switching_Owned_Team_Rebuilds_Report_Without_Stale_Roster()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var intel = provider.GetRequiredService<IFantasyTeamIntelligenceService>();

        var league = leagues.CurrentLeague!;
        var before = intel.GetReport();
        Assert.Equal(1, before.SelectedRosterId);

        Assert.True(leagues.SelectUserTeam(league.Id, 2));
        var after = intel.GetReport();

        Assert.Equal(2, after.SelectedRosterId);
        Assert.NotEqual(before.TeamName, after.TeamName);
        Assert.NotEqual(
            string.Join("|", before.RosterIntelligence.Select(r => r.PlayerId).OrderBy(id => id)),
            string.Join("|", after.RosterIntelligence.Select(r => r.PlayerId).OrderBy(id => id)));
    }

    [Fact]
    public void Switching_League_Rebuilds_Observations_And_Recommendations()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var intel = provider.GetRequiredService<IFantasyTeamIntelligenceService>();

        var friends = leagues.GetAllLeagues().Single(l => l.Name == "Friends League");
        var dynasty = leagues.GetAllLeagues().Single(l => l.Name == "Dynasty League");

        leagues.SelectLeague(friends.Id);
        var friendsReport = intel.GetReport();
        Assert.Equal(friends.Id, friendsReport.LeagueId);
        Assert.Equal("PPR", friendsReport.ScoringLabel);

        leagues.SelectLeague(dynasty.Id);
        var dynastyReport = intel.GetReport();

        Assert.Equal(dynasty.Id, dynastyReport.LeagueId);
        Assert.Equal("Half PPR", dynastyReport.ScoringLabel);
        Assert.DoesNotContain(dynastyReport.RosterIntelligence, r =>
            friendsReport.LeagueId == dynastyReport.LeagueId);
        Assert.NotEqual(friendsReport.LeagueName, dynastyReport.LeagueName);
    }

    [Fact]
    public void StartSit_Includes_Reasons_And_Does_Not_Fabricate_When_Empty_Context()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var intel = provider.GetRequiredService<IFantasyTeamIntelligenceService>();

        var report = intel.GetReport();
        Assert.True(report.HasRosterPlayers);
        Assert.All(report.StartSit, rec =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rec.PlayerName));
            Assert.InRange(rec.Confidence, 1, 100);
            Assert.NotEmpty(rec.Reasons);
            Assert.True(rec.Reasons.Count <= 3);
        });
    }

    [Fact]
    public void WhatMatters_And_Alerts_Are_Bounded()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var intel = provider.GetRequiredService<IFantasyTeamIntelligenceService>();
        var report = intel.GetReport();

        Assert.True(report.WhatMatters.Count <= 4);
        Assert.True(report.Alerts.Count <= 8);
        Assert.All(report.WhatMatters, item => Assert.False(string.IsNullOrWhiteSpace(item)));
    }

    [Fact]
    public void Mock_Teams_Have_Distinct_Rosters_For_Verification()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var league = leagues.CurrentLeague!;
        var team1 = leagues.GetTeams(league.Id).Single(t => t.RosterId == 1);
        var team2 = leagues.GetTeams(league.Id).Single(t => t.RosterId == 2);

        Assert.NotEmpty(team1.PlayerIds);
        Assert.NotEmpty(team2.PlayerIds);
        Assert.False(team1.PlayerIds.SequenceEqual(team2.PlayerIds));
    }
}
