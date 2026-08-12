using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Infrastructure.Leagues;

namespace Playbook.Tests;

public class LeagueUserTeamStoreTests
{
    [Fact]
    public void Connected_External_League_Id_Survives_A_New_Store_Instance_At_The_Same_Path()
    {
        var fileName = $"league-user-teams-tests-{Guid.NewGuid():N}.json";
        var first = new LeagueUserTeamStore(NullLogger<LeagueUserTeamStore>.Instance, fileName);

        first.SaveConnectedExternalLeagueId("123456789");

        // A brand-new instance (simulating a process restart / redeploy) reading the same file.
        var second = new LeagueUserTeamStore(NullLogger<LeagueUserTeamStore>.Instance, fileName);
        var ids = second.GetConnectedExternalLeagueIds();

        Assert.Contains("123456789", ids);
        File.Delete(first.StorePath);
    }

    [Fact]
    public void Saving_The_Same_External_League_Id_Twice_Does_Not_Duplicate()
    {
        var fileName = $"league-user-teams-tests-{Guid.NewGuid():N}.json";
        var store = new LeagueUserTeamStore(NullLogger<LeagueUserTeamStore>.Instance, fileName);

        store.SaveConnectedExternalLeagueId("555");
        store.SaveConnectedExternalLeagueId("555");

        Assert.Single(store.GetConnectedExternalLeagueIds(), id => id == "555");
        File.Delete(store.StorePath);
    }

    [Fact]
    public void Multiple_Connected_Leagues_Are_All_Persisted()
    {
        var fileName = $"league-user-teams-tests-{Guid.NewGuid():N}.json";
        var store = new LeagueUserTeamStore(NullLogger<LeagueUserTeamStore>.Instance, fileName);

        store.SaveConnectedExternalLeagueId("111");
        store.SaveConnectedExternalLeagueId("222");

        var ids = store.GetConnectedExternalLeagueIds();
        Assert.Contains("111", ids);
        Assert.Contains("222", ids);
        File.Delete(store.StorePath);
    }

    [Fact]
    public void Selected_Roster_And_Connected_League_Ids_Both_Survive_In_The_Same_Store()
    {
        var fileName = $"league-user-teams-tests-{Guid.NewGuid():N}.json";
        var store = new LeagueUserTeamStore(NullLogger<LeagueUserTeamStore>.Instance, fileName);

        store.SaveConnectedExternalLeagueId("999");
        store.SaveSelectedRosterId("sleeper:999", 4);

        var reloaded = new LeagueUserTeamStore(NullLogger<LeagueUserTeamStore>.Instance, fileName);
        Assert.Contains("999", reloaded.GetConnectedExternalLeagueIds());
        Assert.True(reloaded.TryGetSelectedRosterId("sleeper:999", out var rosterId));
        Assert.Equal(4, rosterId);
        File.Delete(store.StorePath);
    }

    [Fact]
    public void No_Connected_Leagues_Before_Any_Are_Saved()
    {
        var fileName = $"league-user-teams-tests-{Guid.NewGuid():N}.json";
        var store = new LeagueUserTeamStore(NullLogger<LeagueUserTeamStore>.Instance, fileName);

        Assert.Empty(store.GetConnectedExternalLeagueIds());
    }
}
