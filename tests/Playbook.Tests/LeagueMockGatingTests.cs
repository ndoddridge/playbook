using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Playbook.Application.Leagues;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Application.Players;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Infrastructure.Leagues;

namespace Playbook.Tests;

/// <summary>
/// Personal-use product behavior: with mock leagues disabled, uploaded/connected leagues are the
/// only source of truth and no demo team is ever auto-created.
/// </summary>
public class LeagueMockGatingTests
{
    [Fact]
    public void No_Current_League_At_Startup_When_Mock_Is_Disabled_And_Nothing_Is_Connected()
    {
        var service = CreateService(mockEnabled: false);

        Assert.Null(service.GetCurrentLeague());
    }

    [Fact]
    public void GetAllLeagues_Is_Empty_When_Mock_Is_Disabled_And_Nothing_Is_Connected()
    {
        var service = CreateService(mockEnabled: false);

        Assert.Empty(service.GetAllLeagues());
    }

    [Fact]
    public void SelectLeague_Ignores_Mock_League_Ids_When_Mock_Is_Disabled()
    {
        var service = CreateService(mockEnabled: false);
        var mockOnlyLeagueId = Guid.Parse("11111111-1111-1111-1111-111111111111"); // "Friends League" id

        service.SelectLeague(mockOnlyLeagueId);

        Assert.Null(service.GetCurrentLeague());
    }

    [Fact]
    public void Mock_Leagues_Still_Available_When_Explicitly_Enabled()
    {
        var service = CreateService(mockEnabled: true);

        Assert.NotEmpty(service.GetAllLeagues());
        Assert.NotNull(service.GetCurrentLeague());
    }

    private static CompositeLeagueService CreateService(bool mockEnabled)
    {
        var store = new LeagueUserTeamStore(
            NullLogger<LeagueUserTeamStore>.Instance,
            $"league-user-teams-tests-{Guid.NewGuid():N}.json");

        return new CompositeLeagueService(
            new MockLeagueService(new EmptyPlayerService()),
            new NullSleeperLeagueClient(),
            store,
            new LeagueSyncStatus(),
            Options.Create(new LeagueOptions { EnableMockLeagues = mockEnabled }),
            NullLogger<CompositeLeagueService>.Instance);
    }

    private sealed class EmptyPlayerService : IPlayerService
    {
        public IReadOnlyList<Player> GetAllPlayers() => [];
        public Player? GetPlayer(Guid playerId) => null;
        public PlayerProfile? GetPlayerProfile(Guid playerId) => null;
        public IReadOnlyList<Player> SearchPlayers(string? query) => [];
        public void Refresh()
        {
        }
    }

    private sealed class NullSleeperLeagueClient : ISleeperLeagueClient
    {
        public Task<SleeperLeagueSnapshot?> GetLeagueSnapshotAsync(
            string leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SleeperLeagueSnapshot?>(null);

        public Task<IReadOnlyList<SleeperDraftSummary>> GetDraftsForLeagueAsync(
            string leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SleeperDraftSummary>>([]);

        public Task<SleeperDraftSnapshot?> GetDraftAsync(
            string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SleeperDraftSnapshot?>(null);

        public Task<IReadOnlyList<SleeperDraftPickSnapshot>> GetDraftPicksAsync(
            string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SleeperDraftPickSnapshot>>([]);
    }
}
