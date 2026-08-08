using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Leagues;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Application.Players.Data;
using Playbook.Core.Leagues;
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
    }

    [Fact]
    public async Task ConnectSleeperLeague_Loads_Settings_And_Rosters()
    {
        var handler = new StubSleeperHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.sleeper.app/v1/")
        };
        var factory = new FixedHttpClientFactory(httpClient);
        var sync = new LeagueSyncStatus();
        var service = new CompositeLeagueService(
            new MockLeagueService(),
            new SleeperLeagueClient(factory, NullLogger<SleeperLeagueClient>.Instance),
            sync,
            NullLogger<CompositeLeagueService>.Instance);

        var result = await service.ConnectSleeperLeagueAsync("1185754400042356736");

        Assert.True(result.Succeeded, result.Error);
        Assert.NotNull(result.League);
        Assert.Equal(LeagueDataSource.Sleeper, result.League!.DataSource);
        Assert.Equal("Survivor Dynasty League", result.League.Name);
        Assert.Equal(2025, result.League.Season);
        Assert.Equal(14, result.League.NumberOfTeams);
        Assert.Equal(ScoringType.Ppr, result.League.ScoringType);
        Assert.Equal(1.0m, result.League.ReceptionPoints);
        Assert.Equal(LeagueType.Dynasty, result.League.LeagueType);
        Assert.Equal("1185754400042356736", result.League.ExternalId);
        Assert.Equal(2, result.Teams.Count);
        Assert.Contains(result.Teams, t => t.DisplayName == "Owner One");
        Assert.True(result.Teams.All(t => t.PlayerIds.Count > 0));
        Assert.Equal(4, service.GetAllLeagues().Count);
        Assert.Equal(result.League.Id, service.GetCurrentLeague()?.Id);
        Assert.Equal(1, sync.LiveLeaguesLoaded);
        Assert.Equal(2, sync.TeamsLoaded);
    }

    [Fact]
    public async Task ConnectSleeperLeague_HalfPpr_Cityline_Fixture()
    {
        var handler = new StubSleeperHandler(halfPpr: true);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.sleeper.app/v1/")
        };
        var service = new CompositeLeagueService(
            new MockLeagueService(),
            new SleeperLeagueClient(new FixedHttpClientFactory(httpClient), NullLogger<SleeperLeagueClient>.Instance),
            new LeagueSyncStatus(),
            NullLogger<CompositeLeagueService>.Instance);

        var result = await service.ConnectSleeperLeagueAsync("1255233337891504128");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(ScoringType.HalfPpr, result.League!.ScoringType);
        Assert.Equal(0.5m, result.League.ReceptionPoints);
        Assert.Equal(LeagueType.Redraft, result.League.LeagueType);
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
        var handler = new StubSleeperHandler(notFound: true);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.sleeper.app/v1/")
        };
        var service = new CompositeLeagueService(
            new MockLeagueService(),
            new SleeperLeagueClient(new FixedHttpClientFactory(httpClient), NullLogger<SleeperLeagueClient>.Instance),
            new LeagueSyncStatus(),
            NullLogger<CompositeLeagueService>.Instance);

        var result = await service.ConnectSleeperLeagueAsync("0000000000000000000");

        Assert.False(result.Succeeded);
        Assert.Equal(3, service.GetAllLeagues().Count);
        Assert.Equal(LeagueDataSource.Mock, service.GetCurrentLeague()?.DataSource);
    }

    [Fact]
    public async Task LeagueState_Connect_Raises_Changed_And_Wires_Player_Context()
    {
        var handler = new StubSleeperHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.sleeper.app/v1/")
        };
        var leagueService = new CompositeLeagueService(
            new MockLeagueService(),
            new SleeperLeagueClient(new FixedHttpClientFactory(httpClient), NullLogger<SleeperLeagueClient>.Instance),
            new LeagueSyncStatus(),
            NullLogger<CompositeLeagueService>.Instance);
        var state = new LeagueStateService(leagueService);
        var changed = 0;
        state.Changed += () => changed++;

        var result = await state.ConnectSleeperLeagueAsync("1185754400042356736");
        var ownedPlayerId = result.Teams[0].PlayerIds[0];
        var team = state.FindTeamForPlayer(ownedPlayerId);

        Assert.True(result.Succeeded);
        Assert.Equal(1, changed);
        Assert.NotNull(team);
        Assert.Equal(ScoringType.Ppr, state.CurrentLeague?.ScoringType);
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
