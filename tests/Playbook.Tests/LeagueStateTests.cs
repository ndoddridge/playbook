using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Core.Leagues;
using Playbook.Infrastructure.Leagues;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class LeagueStateTests
{
    [Fact]
    public void Default_Current_League_Is_Friends_League()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<ILeagueState>();

        Assert.Equal("Friends League", state.GetCurrentLeague()?.Name);
        Assert.Equal(3, state.GetAllLeagues().Count);
    }

    [Fact]
    public void SelectLeague_Updates_Current_And_Raises_Changed()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<ILeagueState>();
        var dynasty = state.GetAllLeagues().Single(l => l.Name == "Dynasty League");
        var changed = 0;
        state.Changed += () => changed++;

        state.SelectLeague(dynasty.Id);

        Assert.Equal("Dynasty League", state.CurrentLeague?.Name);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void MockLeagueService_SelectLeague_Is_Reflected_By_GetCurrentLeague()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var service = new MockLeagueService(provider.GetRequiredService<IPlayerService>());
        var work = service.GetAllLeagues().Single(l => l.Name == "Work League");

        service.SelectLeague(work.Id);

        Assert.Equal("Work League", service.GetCurrentLeague()?.Name);
        Assert.Equal(LeagueDataSource.Mock, work.DataSource);
    }

    [Fact]
    public void Mock_Leagues_Expose_Demo_Teams_With_Seeded_Rosters()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<ILeagueState>();
        var players = provider.GetRequiredService<IPlayerService>();
        var teams = state.GetCurrentTeams();

        Assert.NotEmpty(teams);
        Assert.All(teams, t => Assert.NotEmpty(t.PlayerIds));
        Assert.NotNull(state.CurrentUserTeam);
        Assert.Equal(1, state.CurrentUserTeam!.RosterId);
        Assert.NotEmpty(state.CurrentUserTeam.StarterIds);

        // Demo roster ids must resolve in the active catalog (mock or live), not use orphan GUIDs.
        Assert.All(
            state.CurrentUserTeam.PlayerIds,
            id => Assert.NotNull(players.GetPlayer(id)));
    }

    [Fact]
    public void Mock_SelectUserTeam_Updates_Current_User_Team()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<ILeagueState>();
        var changed = 0;
        state.Changed += () => changed++;

        Assert.True(state.SelectUserTeam(state.CurrentLeague!.Id, 2));

        Assert.Equal(2, state.CurrentUserTeam?.RosterId);
        Assert.Equal(1, changed);
    }
}
