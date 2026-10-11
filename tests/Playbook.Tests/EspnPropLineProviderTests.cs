using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Playbook.Application.Players;
using Playbook.Application.Predictions;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Infrastructure.Players;
using Playbook.Infrastructure.Predictions;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// ESPN's public odds, in the shapes its scoreboard and core prop-bet endpoints answer with
/// (trimmed from real October 2026 responses).
/// </summary>
public class EspnPropLineProviderTests
{
    private const string Scoreboard = """
    {
      "week": { "number": 5 },
      "season": { "type": 2, "year": 2026 },
      "events": [
        {
          "id": "401872980", "date": "2026-10-09T00:15Z",
          "status": { "type": { "name": "STATUS_FINAL", "completed": true } },
          "competitions": [ { "competitors": [
            { "homeAway": "home", "team": { "abbreviation": "DAL" } },
            { "homeAway": "away", "team": { "abbreviation": "TB" } } ], "odds": null } ]
        },
        {
          "id": "401872981", "date": "2026-10-11T13:30Z",
          "status": { "type": { "name": "STATUS_SCHEDULED", "completed": false } },
          "competitions": [ {
            "competitors": [
              { "homeAway": "home", "team": { "abbreviation": "JAX" } },
              { "homeAway": "away", "team": { "abbreviation": "PHI" } } ],
            "odds": [ {
              "provider": { "id": "100", "name": "DraftKings" },
              "details": "JAX -7.5", "overUnder": 41.5, "spread": -7.5,
              "moneyline": { "home": { "close": { "odds": "-395" } }, "away": { "close": { "odds": "+310" } } },
              "pointSpread": { "home": { "close": { "line": "-7.5", "odds": "-108" } }, "away": { "close": { "line": "+7.5", "odds": "-112" } } },
              "total": { "over": { "close": { "line": "o41.5", "odds": "-112" } }, "under": { "close": { "line": "u41.5", "odds": "-108" } } }
            } ]
          } ]
        }
      ]
    }
    """;

    private const string NextWeek = """
    {
      "events": [ {
        "id": "401872995", "date": "2026-10-16T00:15Z",
        "status": { "type": { "name": "STATUS_SCHEDULED", "completed": false } },
        "competitions": [ {
          "competitors": [
            { "homeAway": "home", "team": { "abbreviation": "WSH" } },
            { "homeAway": "away", "team": { "abbreviation": "NYG" } } ],
          "odds": [ { "provider": { "id": "100", "name": "DraftKings" }, "overUnder": 44.5, "spread": -3.0 } ]
        } ]
      } ]
    }
    """;

    private static string Athlete(string id) =>
        $$"""{ "$ref": "http://sports.core.api.espn.com/v2/sports/football/leagues/nfl/seasons/2026/athletes/{{id}}?lang=en&region=us" }""";

    private static string Prop(string athleteId, string type, decimal? value) =>
        $$"""{ "athlete": {{Athlete(athleteId)}}, "type": { "id": "1", "name": "{{type}}" }, "lastUpdated": "2026-10-11T01:30Z", "current": {{(value is null ? "{}" : $$"""{ "target": { "value": {{value}} } }""")}} }""";

    // Each line is listed twice, once per side. The page split mirrors the real 1,664-item answer.
    private static readonly string PropsPage1 = $$"""
    { "count": 9, "pageCount": 2, "items": [
      {{Prop("4040715", "Total Passing Yards (incl. overtime)", 189.5m)}},
      {{Prop("4040715", "Total Passing Yards (incl. overtime)", 189.5m)}},
      {{Prop("4040715", "1st Half Total Passing Yards", 99.5m)}},
      {{Prop("3121023", "Total Receptions (incl. overtime)", 4.5m)}},
      {{Prop("3121023", "Total Receptions (incl. overtime)", 4.5m)}},
      {{Prop("3121023", "Total Receptions (incl. overtime)", 5.5m)}}
    ] }
    """;

    private static readonly string PropsPage2 = $$"""
    { "count": 9, "pageCount": 2, "items": [
      {{Prop("3121023", "Anytime Touchdown Scorer", null)}},
      {{Prop("9999999", "Total Rushing Yards (incl. overtime)", 27.5m)}},
      { "team": { "$ref": "http://sports.core.api.espn.com/v2/sports/football/leagues/nfl/seasons/2026/teams/21" }, "type": { "name": "Team Total Points" }, "current": { "target": { "value": 17.5 } } }
    ] }
    """;

    private static readonly Player Quarterback = NewPlayer("Trevor Lawrence", "JAX", Position.QB);
    private static readonly Player Receiver = NewPlayer("Brian Thomas", "JAX", Position.WR);

    [Fact]
    public async Task Loads_Game_Lines_And_Player_Props_Without_A_Key()
    {
        var (provider, requested) = CreateProvider();

        var lines = await provider.GetPropLinesAsync();

        // The finished game is skipped; this week's and next week's games are loaded.
        Assert.DoesNotContain(lines, l => l.Event.EventId == "401872980");
        Assert.All(lines, l => Assert.Equal("ESPN", l.Source));
        Assert.All(lines, l => Assert.Equal(PropLineFreshness.Live, l.Freshness));

        var jax = lines.Where(l => l.Event.EventId == "401872981").ToList();
        Assert.All(jax, l => Assert.Equal(("JAX", "PHI"), (l.Event.HomeTeam, l.Event.AwayTeam)));
        Assert.All(jax, l => Assert.Equal(NflSeasonPhase.RegularSeason, l.Event.PhaseHint));
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 13, 30, 0, TimeSpan.Zero), jax[0].Event.CommenceTime);
        Assert.All(jax, l => Assert.Equal((2026, 5), (l.Event.Season, l.Event.Week)));

        var total = Assert.Single(jax, l => l.Market == PredictionMarketType.GameTotal);
        Assert.Equal((41.5m, -112, -108), (total.Line!.Value, total.AmericanOddsOver!.Value, total.AmericanOddsUnder!.Value));
        Assert.Equal("DraftKings", total.Bookmaker);

        var spread = Assert.Single(jax, l => l.Market == PredictionMarketType.Spread);
        Assert.Equal(("JAX", -7.5m, -108), (spread.TeamName, spread.Line!.Value, spread.AmericanOddsOver!.Value));

        var winner = Assert.Single(jax, l => l.Market == PredictionMarketType.Winner);
        Assert.Equal((-395, 310), (winner.AmericanOddsOver!.Value, winner.AmericanOddsUnder!.Value));

        var passing = Assert.Single(jax, l => l.Market == PredictionMarketType.PassingYards);
        Assert.Equal((Quarterback.Id, 189.5m), (passing.PlayerId!.Value, passing.Line!.Value));
        Assert.Equal("Trevor Lawrence", passing.PlayerName);
        Assert.Null(passing.AmericanOddsOver);

        // Where a player has more than one value, the line listed most often is the main one.
        var receptions = Assert.Single(jax, l => l.Market == PredictionMarketType.Receptions);
        Assert.Equal((Receiver.Id, 4.5m), (receptions.PlayerId!.Value, receptions.Line!.Value));

        // Page two was read: the anytime touchdown offer, with the conventional 0.5 line.
        var touchdown = Assert.Single(jax, l => l.Market == PredictionMarketType.AnytimeTouchdown);
        Assert.Equal((Receiver.Id, 0.5m), (touchdown.PlayerId!.Value, touchdown.Line!.Value));

        // Not modeled or not a known player: first-half lines, team totals, an unmatched athlete.
        Assert.DoesNotContain(jax, l => l.Market == PredictionMarketType.RushingYards);
        Assert.DoesNotContain(jax, l => l.Market == PredictionMarketType.TeamTotal);
        Assert.Equal(6, jax.Count);

        // Next week's game, with ESPN's WSH written as Playbook's WAS.
        var next = lines.Where(l => l.Event.EventId == "401872995").ToList();
        Assert.All(next, l => Assert.Equal(("WAS", "NYG"), (l.Event.HomeTeam, l.Event.AwayTeam)));
        Assert.All(next, l => Assert.Equal((2026, 6), (l.Event.Season, l.Event.Week)));
        Assert.Contains(next, l => l.Market == PredictionMarketType.GameTotal && l.Line == 44.5m);
        Assert.Contains(next, l => l.Market == PredictionMarketType.Spread && l.Line == -3.0m);

        Assert.Contains(requested, u => u.Contains("scoreboard?seasontype=2&week=6&dates=2026"));
        Assert.Contains(requested, u => u.Contains("/events/401872981/competitions/401872981/odds/100/propBets") && u.Contains("page=2"));
    }

    [Fact]
    public async Task A_Failed_Prop_Request_Keeps_The_Game_Lines()
    {
        var (provider, _) = CreateProvider(failProps: true);

        var lines = await provider.GetPropLinesAsync();

        Assert.Contains(lines, l => l.Event.EventId == "401872981" && l.Market == PredictionMarketType.GameTotal);
        Assert.DoesNotContain(lines, l => l.PlayerId is not null);
    }

    [Theory]
    [InlineData("-7.5", -7.5)]
    [InlineData("+3", 3)]
    [InlineData("o41.5", 41.5)]
    [InlineData("u41.5", 41.5)]
    public void Parses_Lines(string text, double expected) =>
        Assert.Equal((decimal)expected, EspnPropLineProvider.ParseLine(text));

    [Theory]
    [InlineData("-395", -395)]
    [InlineData("+310", 310)]
    [InlineData("EVEN", 100)]
    public void Parses_American_Odds(string text, int expected) =>
        Assert.Equal(expected, EspnPropLineProvider.ParseAmerican(text));

    [Fact]
    public void The_Calendar_Keeps_A_Week_The_Provider_States()
    {
        var calendar = new NflCalendarService(new StubFactory(new StubHandler(_ => null)), NullLogger<NflCalendarService>.Instance);
        var context = new NflSeasonContext
        {
            Season = 2026, Phase = NflSeasonPhase.RegularSeason, Week = 6, DisplayWeek = 6,
            ResolvedAt = DateTimeOffset.UtcNow, Source = "test"
        };
        FootballEvent Game(string id, int week, DateTimeOffset kickoff) => new()
        {
            EventId = id, HomeTeam = "JAX", AwayTeam = "PHI", CommenceTime = kickoff,
            PhaseHint = NflSeasonPhase.RegularSeason, Season = 2026, Week = week
        };

        var kickoff = new DateTimeOffset(2026, 10, 18, 17, 0, 0, TimeSpan.Zero);
        var enriched = calendar.EnrichEvents([Game("a", 6, kickoff), Game("b", 7, kickoff.AddDays(7))], context);

        Assert.Equal([6, 7], enriched.Select(e => e.Week));
    }

    private static (EspnPropLineProvider Provider, List<string> Requested) CreateProvider(bool failProps = false)
    {
        var requested = new List<string>();
        var handler = new StubHandler(url =>
        {
            requested.Add(url);
            if (url.Contains("/propBets"))
            {
                if (failProps)
                {
                    return null;
                }

                return url.Contains("page=2") ? PropsPage2 : PropsPage1;
            }

            if (url.Contains("week=6"))
            {
                return NextWeek;
            }

            return url.EndsWith("/scoreboard") ? Scoreboard : null;
        });

        var identities = new PlayerIdentityDirectory();
        identities.Upsert(new PlaybookPlayerIdentity { PlaybookId = Quarterback.Id, FullName = Quarterback.FullName, EspnId = "4040715" });
        identities.Upsert(new PlaybookPlayerIdentity { PlaybookId = Receiver.Id, FullName = Receiver.FullName, EspnId = "3121023" });

        var provider = new EspnPropLineProvider(
            new StubFactory(handler),
            new StubPlayers([Quarterback, Receiver]),
            identities,
            Options.Create(new PropLineOptions { Provider = PropLineProviderKind.Espn }),
            NullLogger<EspnPropLineProvider>.Instance);
        return (provider, requested);
    }

    private static Player NewPlayer(string name, string team, Position position)
    {
        var parts = name.Split(' ', 2);
        return new Player
        {
            Id = Guid.NewGuid(),
            FullName = name,
            FirstName = parts[0],
            LastName = parts[1],
            Position = position,
            Team = team,
            Status = PlayerStatus.Active,
        };
    }

    private sealed class StubHandler(Func<string, string?> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = respond(request.RequestUri!.ToString());
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubPlayers(IReadOnlyList<Player> players) : IPlayerService
    {
        public IReadOnlyList<Player> GetAllPlayers() => players;

        public Player? GetPlayer(Guid playerId) => players.FirstOrDefault(p => p.Id == playerId);

        public PlayerProfile? GetPlayerProfile(Guid playerId) => null;

        public IReadOnlyList<Player> SearchPlayers(string? query) => players;

        public void Refresh()
        {
        }
    }
}
