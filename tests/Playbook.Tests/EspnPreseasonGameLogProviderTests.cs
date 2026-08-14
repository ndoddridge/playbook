using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Players;
using Playbook.Core.Players;
using Playbook.Infrastructure.Players;
using Playbook.Infrastructure.Stats;

namespace Playbook.Tests;

public class EspnPreseasonGameLogProviderTests
{
    private static readonly Guid ResolvedPlayerId = SleeperPlayerIds.ToPlaybookId("9001");

    [Fact]
    public async Task Maps_Real_Shaped_Boxscore_Into_Tagged_Preseason_GameStats()
    {
        var identities = BuildIdentities();
        var handler = new StubEspnHandler();
        var provider = new EspnPreseasonGameLogProvider(
            new FixedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://site.api.espn.com/apis/site/v2/") }),
            identities,
            NullLogger<EspnPreseasonGameLogProvider>.Instance);

        var logs = await provider.GetPreseasonGameLogsAsync(2026, new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero));

        var log = Assert.Single(logs);
        Assert.Equal(ResolvedPlayerId, log.PlayerId);
        Assert.Equal("preseason", log.SeasonType);
        Assert.Equal(2026, log.Season);
        Assert.Equal(1, log.Week);
        Assert.Equal(21, log.PassCompletions);
        Assert.Equal(34, log.PassAttempts);
        Assert.Equal(180, log.PassYards);
        Assert.Equal(2, log.PassTouchdowns);
        Assert.Equal(0, log.PassInterceptions);
        Assert.Equal(3, log.RushAttempts);
        Assert.Equal(12, log.RushYards);
        Assert.Equal("ESPN", log.SourceProvider);
    }

    [Fact]
    public async Task Never_Fabricates_An_Identity_For_An_Unresolved_Espn_Athlete()
    {
        // Directory only knows the one resolved athlete — "9999999" in the boxscore has no
        // crosswalk entry and must be silently skipped, not guessed at.
        var identities = BuildIdentities();
        var handler = new StubEspnHandler();
        var provider = new EspnPreseasonGameLogProvider(
            new FixedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://site.api.espn.com/apis/site/v2/") }),
            identities,
            NullLogger<EspnPreseasonGameLogProvider>.Instance);

        var logs = await provider.GetPreseasonGameLogsAsync(2026, new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero));

        Assert.DoesNotContain(logs, l => l.PlayerId == Guid.Empty);
        Assert.Single(logs); // only the resolved athlete, never the unmatched one
    }

    [Fact]
    public async Task Second_Call_For_The_Same_Date_Is_Served_From_Cache()
    {
        var identities = BuildIdentities();
        var handler = new StubEspnHandler();
        var provider = new EspnPreseasonGameLogProvider(
            new FixedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://site.api.espn.com/apis/site/v2/") }),
            identities,
            NullLogger<EspnPreseasonGameLogProvider>.Instance);

        var date = new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);
        await provider.GetPreseasonGameLogsAsync(2026, date);
        var callsAfterFirst = handler.RequestCount;
        await provider.GetPreseasonGameLogsAsync(2026, date);

        Assert.Equal(callsAfterFirst, handler.RequestCount);
    }

    [Fact]
    public async Task No_Real_Game_On_The_Date_Returns_Empty_Not_Fabricated()
    {
        var identities = BuildIdentities();
        var handler = new StubEspnHandler(hasEvent: false);
        var provider = new EspnPreseasonGameLogProvider(
            new FixedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://site.api.espn.com/apis/site/v2/") }),
            identities,
            NullLogger<EspnPreseasonGameLogProvider>.Instance);

        var logs = await provider.GetPreseasonGameLogsAsync(2026, new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(logs);
    }

    private static PlayerIdentityDirectory BuildIdentities()
    {
        var directory = new PlayerIdentityDirectory();
        directory.ReplaceAll(
        [
            new PlaybookPlayerIdentity
            {
                PlaybookId = ResolvedPlayerId,
                FullName = "Resolved Athlete",
                Position = "QB",
                SleeperId = "9001",
                EspnId = "4428993"
            }
        ]);
        return directory;
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubEspnHandler(bool hasEvent = true) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;

            if (path.Contains("/scoreboard", StringComparison.Ordinal))
            {
                return Json(hasEvent
                    ? new
                    {
                        events = new object[]
                        {
                            new
                            {
                                id = "401873271",
                                season = new { year = 2026 },
                                week = new { number = 1 },
                                competitions = new object[] { new { status = new { type = new { completed = true } } } }
                            }
                        }
                    }
                    : new { events = Array.Empty<object>() });
            }

            if (path.Contains("/summary", StringComparison.Ordinal))
            {
                return Json(new
                {
                    boxscore = new
                    {
                        players = new object[]
                        {
                            new
                            {
                                statistics = new object[]
                                {
                                    new
                                    {
                                        name = "passing",
                                        keys = new[]
                                        {
                                            "completions/passingAttempts", "passingYards",
                                            "yardsPerPassAttempt", "passingTouchdowns", "interceptions",
                                            "sacks-sackYardsLost", "adjQBR", "QBRating"
                                        },
                                        athletes = new object[]
                                        {
                                            new
                                            {
                                                athlete = new { id = "4428993", displayName = "Resolved Athlete" },
                                                stats = new[] { "21/34", "180", "5.3", "2", "0", "0-0", "", "95.2" }
                                            }
                                        }
                                    },
                                    new
                                    {
                                        name = "rushing",
                                        keys = new[]
                                        {
                                            "rushingAttempts", "rushingYards", "yardsPerRushAttempt",
                                            "rushingTouchdowns", "longRushing"
                                        },
                                        athletes = new object[]
                                        {
                                            new
                                            {
                                                athlete = new { id = "4428993", displayName = "Resolved Athlete" },
                                                stats = new[] { "3", "12", "4.0", "0", "6" }
                                            },
                                            new
                                            {
                                                athlete = new { id = "9999999", displayName = "Unresolved Athlete" },
                                                stats = new[] { "2", "5", "2.5", "0", "3" }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
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
