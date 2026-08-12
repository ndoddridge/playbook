using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Playbook.Application.Leagues;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Infrastructure.Leagues;

namespace Playbook.Tests;

public class SleeperLeagueIntegrationTests
{
    [Theory]
    [InlineData(1.0, ScoringType.Ppr)]
    [InlineData(0.5, ScoringType.HalfPpr)]
    [InlineData(0.0, ScoringType.Standard)]
    [InlineData(0.25, ScoringType.Standard)]
    public void SleeperScoringMapper_Maps_Reception_Points(double rec, ScoringType expected)
    {
        var (format, points) = SleeperScoringMapper.MapReceptionScoring(
            new Dictionary<string, double> { ["rec"] = rec });

        Assert.Equal(expected, format);
        Assert.Equal((decimal)rec, points);
    }

    [Theory]
    [InlineData(0, LeagueType.Redraft)]
    [InlineData(1, LeagueType.Keeper)]
    [InlineData(2, LeagueType.Dynasty)]
    public void SleeperScoringMapper_Maps_League_Type(int sleeperType, LeagueType expected) =>
        Assert.Equal(expected, SleeperScoringMapper.MapLeagueType(sleeperType));

    [Fact]
    public void CompositeLeagueService_Keeps_Mock_Catalog_By_Default()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueService>();

        Assert.Equal(3, leagues.GetAllLeagues().Count);
        Assert.All(leagues.GetAllLeagues(), l => Assert.Equal(LeagueDataSource.Mock, l.DataSource));
        Assert.Equal("Friends League", leagues.GetCurrentLeague()?.Name);
        Assert.True(leagues.GetCurrentLeague()!.IsSetupComplete);
        Assert.NotNull(leagues.GetCurrentUserTeam());
    }

    [Fact]
    public async Task ConnectSleeperLeague_Requires_Team_Selection_Before_Current()
    {
        var service = CreateService(out var store);

        var result = await service.ConnectSleeperLeagueAsync("1185754400042356736");

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.NeedsTeamSelection);
        Assert.False(result.IsSetupComplete);
        Assert.Null(result.League!.SelectedRosterId);
        Assert.Equal(2, result.Teams.Count);
        Assert.Equal("Friends League", service.GetCurrentLeague()?.Name);
        Assert.Equal(4, service.GetAllLeagues().Count);

        var confirmed = service.SelectUserTeam(result.League.Id, result.Teams[1].RosterId);
        Assert.True(confirmed);
        Assert.Equal(result.League.Id, service.GetCurrentLeague()?.Id);
        Assert.Equal(result.Teams[1].RosterId, service.GetCurrentLeague()?.SelectedRosterId);
        Assert.Equal(result.Teams[1].RosterId, service.GetCurrentUserTeam()?.RosterId);
        Assert.True(store.TryGetSelectedRosterId(
            ILeagueUserTeamStore.KeyForExternalId("1185754400042356736"),
            out var saved));
        Assert.Equal(result.Teams[1].RosterId, saved);
    }

    [Fact]
    public async Task ConnectSleeperLeague_Restores_Saved_Team_Automatically()
    {
        var store = new LeagueUserTeamStore(
            NullLogger<LeagueUserTeamStore>.Instance,
            $"league-user-teams-{Guid.NewGuid():N}.json");
        store.SaveSelectedRosterId(
            ILeagueUserTeamStore.KeyForExternalId("1185754400042356736"),
            1);

        var service = CreateService(store);

        var result = await service.ConnectSleeperLeagueAsync("1185754400042356736");

        Assert.True(result.Succeeded, result.Error);
        Assert.False(result.NeedsTeamSelection);
        Assert.True(result.IsSetupComplete);
        Assert.Equal(1, result.League!.SelectedRosterId);
        Assert.Equal(1, result.SelectedTeam?.RosterId);
        Assert.Equal(result.League.Id, service.GetCurrentLeague()?.Id);
        Assert.Equal("Owner One", service.GetCurrentUserTeam()?.DisplayName);
    }

    [Fact]
    public async Task SelectUserTeam_Can_Change_Later()
    {
        var service = CreateService(out _);
        var result = await service.ConnectSleeperLeagueAsync("1185754400042356736");
        Assert.True(service.SelectUserTeam(result.League!.Id, 1));
        Assert.Equal(1, service.GetCurrentUserTeam()?.RosterId);

        Assert.True(service.SelectUserTeam(result.League.Id, 2));
        Assert.Equal(2, service.GetCurrentUserTeam()?.RosterId);
        Assert.Equal(2, service.GetCurrentLeague()?.SelectedRosterId);
    }

    [Fact]
    public async Task ConnectSleeperLeague_HalfPpr_Cityline_Fixture()
    {
        var service = CreateService(out _, halfPpr: true);

        var result = await service.ConnectSleeperLeagueAsync("1255233337891504128");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(ScoringType.HalfPpr, result.League!.ScoringType);
        Assert.Equal(0.5m, result.League.ReceptionPoints);
        Assert.Equal(LeagueType.Redraft, result.League.LeagueType);
        Assert.True(result.NeedsTeamSelection);
    }

    [Fact]
    public async Task ConnectSleeperLeague_Missing_Id_Returns_Error()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<ILeagueState>();

        var result = await state.ConnectSleeperLeagueAsync("   ");

        Assert.False(result.Succeeded);
        Assert.Contains("league ID", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, state.GetAllLeagues().Count);
    }

    [Fact]
    public async Task ConnectSleeperLeague_NotFound_Keeps_Mock_Fallback()
    {
        var service = CreateService(out _, notFound: true);

        var result = await service.ConnectSleeperLeagueAsync("0000000000000000000");

        Assert.False(result.Succeeded);
        Assert.Equal(3, service.GetAllLeagues().Count);
        Assert.Equal(LeagueDataSource.Mock, service.GetCurrentLeague()?.DataSource);
    }

    [Fact]
    public async Task LeagueState_SelectUserTeam_Raises_Changed_And_Completes_Setup()
    {
        var service = CreateService(out _);
        var state = new LeagueStateService(service);
        var changed = 0;
        state.Changed += () => changed++;

        var result = await state.ConnectSleeperLeagueAsync("1185754400042356736");
        Assert.True(result.NeedsTeamSelection);
        Assert.Equal("Friends League", state.CurrentLeague?.Name);

        var beforeComplete = changed;
        Assert.True(state.SelectUserTeam(result.League!.Id, result.Teams[0].RosterId));

        Assert.True(changed > beforeComplete);
        Assert.Equal(result.League.Id, state.CurrentLeague?.Id);
        Assert.NotNull(state.CurrentUserTeam);
        Assert.Equal(result.Teams[0].RosterId, state.CurrentUserTeam!.RosterId);
    }

    [Fact]
    public void SelectUserTeam_Rejects_Unknown_Roster()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<ILeagueState>();
        var league = state.CurrentLeague!;

        Assert.False(state.SelectUserTeam(league.Id, 999));
        Assert.Equal(1, state.CurrentUserTeam?.RosterId);
    }

    private static CompositeLeagueService CreateService(out LeagueUserTeamStore store, bool halfPpr = false, bool notFound = false)
    {
        store = new LeagueUserTeamStore(
            NullLogger<LeagueUserTeamStore>.Instance,
            $"league-user-teams-{Guid.NewGuid():N}.json");
        return CreateService(store, halfPpr, notFound);
    }

    private static CompositeLeagueService CreateService(
        ILeagueUserTeamStore store,
        bool halfPpr = false,
        bool notFound = false)
    {
        var handler = new StubSleeperHandler(notFound: notFound, halfPpr: halfPpr);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.sleeper.app/v1/")
        };
        return new CompositeLeagueService(
            new MockLeagueService(new EmptyPlayerService()),
            new SleeperLeagueClient(new FixedHttpClientFactory(httpClient), NullLogger<SleeperLeagueClient>.Instance),
            store,
            new LeagueSyncStatus(),
            Options.Create(new LeagueOptions()),
            NullLogger<CompositeLeagueService>.Instance);
    }

    private sealed class EmptyPlayerService : IPlayerService
    {
        public IReadOnlyList<Player> GetAllPlayers() => [];
        public Player? GetPlayer(Guid playerId) => null;
        public PlayerProfile? GetPlayerProfile(Guid playerId) => null;
        public IReadOnlyList<Player> SearchPlayers(string? query) => [];
        public void Refresh() { }
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubSleeperHandler : HttpMessageHandler
    {
        private readonly bool _notFound;
        private readonly bool _halfPpr;

        public StubSleeperHandler(bool notFound = false, bool halfPpr = false)
        {
            _notFound = notFound;
            _halfPpr = halfPpr;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (_notFound && path.Contains("/league/", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (path.EndsWith("/state/nfl", StringComparison.Ordinal))
            {
                return Json(new { week = 1, display_week = 1, season = "2026" });
            }

            if (path.EndsWith("/users", StringComparison.Ordinal))
            {
                return Json(new object[]
                {
                    new
                    {
                        user_id = "u1",
                        display_name = "Owner One",
                        metadata = new { team_name = "Alpha" }
                    },
                    new
                    {
                        user_id = "u2",
                        display_name = "Owner Two",
                        metadata = new { team_name = "Beta" }
                    }
                });
            }

            if (path.EndsWith("/rosters", StringComparison.Ordinal))
            {
                return Json(new object[]
                {
                    new
                    {
                        roster_id = 1,
                        owner_id = "u1",
                        players = new[] { "4034", "4046" },
                        starters = new[] { "4034" },
                        reserve = Array.Empty<string>(),
                        taxi = Array.Empty<string>(),
                        settings = new { wins = 10, losses = 4, ties = 0, fpts = 1400, fpts_decimal = 50 }
                    },
                    new
                    {
                        roster_id = 2,
                        owner_id = "u2",
                        players = new[] { "4984" },
                        starters = new[] { "4984" },
                        reserve = Array.Empty<string>(),
                        taxi = Array.Empty<string>(),
                        settings = new { wins = 8, losses = 6, ties = 0, fpts = 1300, fpts_decimal = 0 }
                    }
                });
            }

            if (path.Contains("/league/", StringComparison.Ordinal))
            {
                var leagueId = path.Split('/').Last();
                var rec = _halfPpr ? 0.5 : 1.0;
                var type = _halfPpr ? 0 : 2;
                var name = _halfPpr ? "Cityline" : "Survivor Dynasty League";
                var season = _halfPpr ? "2026" : "2025";
                var teams = _halfPpr ? 12 : 14;
                return Json(new
                {
                    league_id = leagueId,
                    name,
                    season,
                    status = "in_season",
                    total_rosters = teams,
                    scoring_settings = new Dictionary<string, double> { ["rec"] = rec, ["pass_td"] = 4 },
                    roster_positions = new[] { "QB", "RB", "RB", "WR", "WR", "TE", "FLEX", "BN" },
                    settings = new { type, num_teams = teams }
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(object payload)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
