using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Leagues;
using Playbook.Application.Users;
using Playbook.Infrastructure.Historical;
using Playbook.Infrastructure.Leagues;

namespace Playbook.Tests.Access;

public class PerUserIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pb-users-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestUser(string id) : IUserContext
    {
        public string UserId { get; } = id;
    }

    private LeagueUserTeamStore TeamStore(string user) =>
        new(NullLogger<LeagueUserTeamStore>.Instance, null, new TestUser(user), _root);

    [Fact]
    public void Connected_Leagues_And_Team_Choices_Are_Private_To_Each_User()
    {
        var alice = TeamStore("alice");
        var bob = TeamStore("bob");

        alice.SaveConnectedExternalLeagueId("111");
        alice.SaveSelectedRosterId(ILeagueUserTeamStore.KeyForExternalId("111"), 4);

        Assert.Empty(bob.GetConnectedExternalLeagueIds());
        Assert.False(bob.TryGetSelectedRosterId(ILeagueUserTeamStore.KeyForExternalId("111"), out _));

        // ...and a fresh instance for Alice (a new session) sees her data again.
        var aliceAgain = TeamStore("alice");
        Assert.Equal(["111"], aliceAgain.GetConnectedExternalLeagueIds());
        Assert.True(aliceAgain.TryGetSelectedRosterId(ILeagueUserTeamStore.KeyForExternalId("111"), out var roster));
        Assert.Equal(4, roster);
    }

    [Fact]
    public void Concurrent_Instances_For_One_User_Share_A_Lock_And_Both_Writes_Persist()
    {
        var a = TeamStore("carol");
        var b = TeamStore("carol");

        Parallel.For(0, 40, i => (i % 2 == 0 ? a : b).SaveConnectedExternalLeagueId("L" + i));

        Assert.Equal(40, TeamStore("carol").GetConnectedExternalLeagueIds().Count);
    }

    [Fact]
    public void Personal_Draft_Stores_Are_Private_Per_User()
    {
        var alice = new PersonalDraftKnowledgeStore(NullLogger<PersonalDraftKnowledgeStore>.Instance, null, new TestUser("alice"), _root);
        var bob = new PersonalDraftKnowledgeStore(NullLogger<PersonalDraftKnowledgeStore>.Instance, null, new TestUser("bob"), _root);
        var historyAlice = new HistoricalLeagueDraftStore(NullLogger<HistoricalLeagueDraftStore>.Instance, null, new TestUser("alice"), _root);
        var historyBob = new HistoricalLeagueDraftStore(NullLogger<HistoricalLeagueDraftStore>.Instance, null, new TestUser("bob"), _root);

        Assert.NotEqual(alice.StorePath, bob.StorePath);
        Assert.NotEqual(historyAlice.StorePath, historyBob.StorePath);
        Assert.Contains(Path.Combine("users", "alice"), alice.StorePath);
    }

    [Fact]
    public void Local_User_Keeps_The_Original_Unnamespaced_File()
    {
        var local = TeamStore(UserDataPaths.LocalUserId);
        var legacy = new LeagueUserTeamStore(NullLogger<LeagueUserTeamStore>.Instance, null, null, _root);

        Assert.Equal(legacy.StorePath, local.StorePath);
        Assert.Equal(Path.Combine(_root, "league-user-teams.json"), local.StorePath);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("has space")]
    public void Unsafe_User_Ids_Can_Never_Reach_The_Filesystem(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            // Blank means "no user": falls back to the shared local file, never a path built from input.
            Assert.Equal(Path.Combine(_root, "x.json"), UserDataPaths.UserFile(_root, new TestUser(id), "x.json"));
            return;
        }

        Assert.Throws<InvalidOperationException>(() => UserDataPaths.UserFile(_root, new TestUser(id), "x.json"));
    }
}
