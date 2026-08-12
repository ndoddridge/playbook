using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Players.Data;
using Playbook.Application.Predictions.Interfaces;

namespace Playbook.Tests;

/// <summary>
/// Drop/Pickup is additive: it must not read or mutate Start/Sit or Quick Picks state.
/// </summary>
public class DropPickupNoInterferenceTests
{
    [Fact]
    public void DropPickup_Report_Does_Not_Change_StartSit_Output()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagueState = provider.GetRequiredService<ILeagueState>();
        var friendsLeague = leagueState.GetAllLeagues().Single(l => l.Name == "Friends League");
        leagueState.SelectLeague(friendsLeague.Id);

        var teamIntel = provider.GetRequiredService<IFantasyTeamIntelligenceService>();
        var dropPickup = provider.GetRequiredService<IDropPickupService>();

        var startSitBefore = teamIntel.GetReport().StartSit
            .Select(r => (r.PlayerId, r.Action, r.DecisionValue))
            .ToList();

        // Generating a Drop/Pickup report must not perturb Start/Sit's own cached result.
        _ = dropPickup.GetReport();

        var startSitAfter = teamIntel.GetReport().StartSit
            .Select(r => (r.PlayerId, r.Action, r.DecisionValue))
            .ToList();

        Assert.Equal(startSitBefore, startSitAfter);
    }

    [Fact]
    public void DropPickup_Report_Does_Not_Change_QuickPicks_Output()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagueState = provider.GetRequiredService<ILeagueState>();
        var friendsLeague = leagueState.GetAllLeagues().Single(l => l.Name == "Friends League");
        leagueState.SelectLeague(friendsLeague.Id);

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        var dropPickup = provider.GetRequiredService<IDropPickupService>();

        var quickPicksBefore = quickPicks.GetAllPredictions()
            .Select(p => (p.PlayerId, p.Market, p.Probability))
            .ToList();

        _ = dropPickup.GetReport();

        var quickPicksAfter = quickPicks.GetAllPredictions()
            .Select(p => (p.PlayerId, p.Market, p.Probability))
            .ToList();

        Assert.Equal(quickPicksBefore, quickPicksAfter);
    }

    [Fact]
    public void StartSit_And_QuickPicks_Still_Work_Alongside_DropPickup_On_The_Same_Roster()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagueState = provider.GetRequiredService<ILeagueState>();
        var friendsLeague = leagueState.GetAllLeagues().Single(l => l.Name == "Friends League");
        leagueState.SelectLeague(friendsLeague.Id);

        var teamIntel = provider.GetRequiredService<IFantasyTeamIntelligenceService>();
        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        var dropPickup = provider.GetRequiredService<IDropPickupService>();

        // All three must resolve and run without throwing against the same league/team context.
        var startSitReport = teamIntel.GetReport();
        var dropPickupReport = dropPickup.GetReport();
        var predictions = quickPicks.GetAllPredictions();

        Assert.NotNull(startSitReport);
        Assert.NotNull(dropPickupReport);
        Assert.NotNull(predictions);
        Assert.Equal(friendsLeague.Id, dropPickupReport.LeagueId);
    }
}
