using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Players;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// The Odds API live prop/game lines. Requires PropLines:OddsApi:ApiKey.
/// </summary>
public sealed class LivePropLineProvider : IPropLineProvider
{
    public const string HttpClientName = "OddsApi";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPlayerService _players;
    private readonly PropLineOptions _options;
    private readonly ILogger<LivePropLineProvider> _logger;

    public LivePropLineProvider(
        IHttpClientFactory httpClientFactory,
        IPlayerService players,
        IOptions<PropLineOptions> options,
        ILogger<LivePropLineProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _players = players;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "TheOddsAPI";

    public async Task<IReadOnlyList<PropLine>> GetPropLinesAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.OddsApi.ApiKey))
        {
            throw new InvalidOperationException(
                "PropLines:OddsApi:ApiKey is empty. Add a key from https://the-odds-api.com/ or use Provider=Mock.");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var watch = Stopwatch.StartNew();
        var sport = Uri.EscapeDataString(_options.OddsApi.SportKey);
        var url =
            $"sports/{sport}/odds?apiKey={Uri.EscapeDataString(_options.OddsApi.ApiKey)}" +
            $"&regions={Uri.EscapeDataString(_options.OddsApi.Regions)}" +
            $"&markets={Uri.EscapeDataString(_options.OddsApi.GameMarkets)}" +
            "&oddsFormat=american";

        var events = await client.GetFromJsonAsync<List<OddsEventDto>>(url, JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? [];

        var players = _players.GetAllPlayers();
        var lines = new List<PropLine>();
        var staleAfter = TimeSpan.FromMinutes(Math.Clamp(_options.StaleAfterMinutes, 15, 24 * 60));
        var maxEvents = Math.Clamp(_options.MaxEvents, 1, 16);

        foreach (var ev in events.OrderBy(e => e.CommenceTime).Take(maxEvents))
        {
            if (string.IsNullOrWhiteSpace(ev.Id))
            {
                continue;
            }

            var footballEvent = new FootballEvent
            {
                EventId = ev.Id!,
                HomeTeam = ev.HomeTeam ?? "Home",
                AwayTeam = ev.AwayTeam ?? "Away",
                CommenceTime = ev.CommenceTime
            };

            foreach (var book in ev.Bookmakers ?? [])
            {
                foreach (var market in book.Markets ?? [])
                {
                    lines.AddRange(MapGameMarket(footballEvent, book, market, staleAfter));
                }
            }

            if (_options.OddsApi.FetchPlayerProps)
            {
                try
                {
                    var propUrl =
                        $"sports/{sport}/events/{Uri.EscapeDataString(ev.Id!)}/odds" +
                        $"?apiKey={Uri.EscapeDataString(_options.OddsApi.ApiKey)}" +
                        $"&regions={Uri.EscapeDataString(_options.OddsApi.Regions)}" +
                        $"&markets={Uri.EscapeDataString(_options.OddsApi.PlayerPropMarkets)}" +
                        "&oddsFormat=american";
                    var detail = await client
                        .GetFromJsonAsync<OddsEventDto>(propUrl, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                    if (detail?.Bookmakers is { Count: > 0 })
                    {
                        var propCount = 0;
                        foreach (var book in detail.Bookmakers)
                        {
                            foreach (var market in book.Markets ?? [])
                            {
                                foreach (var mapped in MapPlayerMarket(footballEvent, book, market, players, staleAfter))
                                {
                                    lines.Add(mapped);
                                    propCount++;
                                    if (propCount >= _options.MaxPlayerPropsPerEvent)
                                    {
                                        break;
                                    }
                                }

                                if (propCount >= _options.MaxPlayerPropsPerEvent)
                                {
                                    break;
                                }
                            }

                            if (propCount >= _options.MaxPlayerPropsPerEvent)
                            {
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Player props fetch failed for event {EventId}", ev.Id);
                }
            }
        }

        watch.Stop();
        _logger.LogInformation(
            "The Odds API loaded {Count} lines from {Events} events in {Ms} ms",
            lines.Count,
            Math.Min(events.Count, maxEvents),
            watch.ElapsedMilliseconds);

        return Deduplicate(lines);
    }

    private static IEnumerable<PropLine> MapGameMarket(
        FootballEvent ev,
        OddsBookmakerDto book,
        OddsMarketDto market,
        TimeSpan staleAfter)
    {
        var key = market.Key ?? string.Empty;
        var updated = market.LastUpdate ?? book.LastUpdate ?? DateTimeOffset.UtcNow;
        var freshness = DateTimeOffset.UtcNow - updated > staleAfter
            ? PropLineFreshness.Stale
            : PropLineFreshness.Live;

        if (key is "totals")
        {
            var over = market.Outcomes?.FirstOrDefault(o =>
                string.Equals(o.Name, "Over", StringComparison.OrdinalIgnoreCase));
            if (over?.Point is null)
            {
                yield break;
            }

            yield return new PropLine
            {
                Id = $"{ev.EventId}:totals:{book.Key}",
                Event = ev,
                Market = PredictionMarketType.GameTotal,
                Line = (decimal)over.Point.Value,
                Bookmaker = book.Title ?? book.Key ?? "book",
                Source = "TheOddsAPI",
                UpdatedAt = updated,
                Freshness = freshness,
                AmericanOddsOver = over.Price,
                AmericanOddsUnder = market.Outcomes?
                    .FirstOrDefault(o => string.Equals(o.Name, "Under", StringComparison.OrdinalIgnoreCase))
                    ?.Price
            };
        }
        else if (key is "spreads")
        {
            var home = market.Outcomes?.FirstOrDefault(o =>
                string.Equals(o.Name, ev.HomeTeam, StringComparison.OrdinalIgnoreCase));
            if (home?.Point is null)
            {
                yield break;
            }

            yield return new PropLine
            {
                Id = $"{ev.EventId}:spreads:{book.Key}",
                Event = ev,
                TeamName = ev.HomeTeam,
                Market = PredictionMarketType.Spread,
                Line = (decimal)home.Point.Value,
                Bookmaker = book.Title ?? book.Key ?? "book",
                Source = "TheOddsAPI",
                UpdatedAt = updated,
                Freshness = freshness,
                AmericanOddsOver = home.Price
            };
        }
        else if (key is "h2h")
        {
            var home = market.Outcomes?.FirstOrDefault(o =>
                string.Equals(o.Name, ev.HomeTeam, StringComparison.OrdinalIgnoreCase));
            yield return new PropLine
            {
                Id = $"{ev.EventId}:h2h:{book.Key}",
                Event = ev,
                TeamName = ev.HomeTeam,
                Market = PredictionMarketType.Winner,
                Line = null,
                Bookmaker = book.Title ?? book.Key ?? "book",
                Source = "TheOddsAPI",
                UpdatedAt = updated,
                Freshness = freshness,
                AmericanOddsOver = home?.Price,
                AmericanOddsUnder = market.Outcomes?
                    .FirstOrDefault(o => string.Equals(o.Name, ev.AwayTeam, StringComparison.OrdinalIgnoreCase))
                    ?.Price
            };
        }
    }

    private static IEnumerable<PropLine> MapPlayerMarket(
        FootballEvent ev,
        OddsBookmakerDto book,
        OddsMarketDto market,
        IReadOnlyList<Core.Players.Player> players,
        TimeSpan staleAfter)
    {
        var marketType = MapPlayerMarketKey(market.Key);
        if (marketType is null)
        {
            yield break;
        }

        var updated = market.LastUpdate ?? book.LastUpdate ?? DateTimeOffset.UtcNow;
        var freshness = DateTimeOffset.UtcNow - updated > staleAfter
            ? PropLineFreshness.Stale
            : PropLineFreshness.Live;

        var overs = (market.Outcomes ?? [])
            .Where(o => string.Equals(o.Name, "Over", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(o.Name, "Yes", StringComparison.OrdinalIgnoreCase) ||
                        (marketType == PredictionMarketType.AnytimeTouchdown && !string.IsNullOrWhiteSpace(o.Description)))
            .ToList();

        foreach (var outcome in overs)
        {
            var playerName = outcome.Description ?? outcome.Name;
            if (string.IsNullOrWhiteSpace(playerName) ||
                string.Equals(playerName, "Over", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(playerName, "Yes", StringComparison.OrdinalIgnoreCase))
            {
                // For anytime TD, description holds player; name may be Yes.
                playerName = outcome.Description;
            }

            if (string.IsNullOrWhiteSpace(playerName))
            {
                continue;
            }

            var player = players.FirstOrDefault(p =>
                string.Equals(p.FullName, playerName, StringComparison.OrdinalIgnoreCase) ||
                p.FullName.EndsWith(playerName, StringComparison.OrdinalIgnoreCase));

            decimal? line = outcome.Point is double pt ? (decimal)pt : null;
            if (marketType == PredictionMarketType.AnytimeTouchdown)
            {
                line ??= 0.5m;
            }

            if (line is null && marketType != PredictionMarketType.AnytimeTouchdown)
            {
                continue;
            }

            yield return new PropLine
            {
                Id = $"{ev.EventId}:{market.Key}:{book.Key}:{playerName}",
                Event = ev,
                PlayerId = player?.Id,
                PlayerName = player?.FullName ?? playerName,
                TeamName = player?.Team,
                Market = marketType.Value,
                Line = line,
                Bookmaker = book.Title ?? book.Key ?? "book",
                Source = "TheOddsAPI",
                UpdatedAt = updated,
                Freshness = freshness,
                AmericanOddsOver = outcome.Price
            };
        }
    }

    private static PredictionMarketType? MapPlayerMarketKey(string? key) => key switch
    {
        "player_pass_yds" => PredictionMarketType.PassingYards,
        "player_rush_yds" => PredictionMarketType.RushingYards,
        "player_reception_yds" => PredictionMarketType.ReceivingYards,
        "player_receptions" => PredictionMarketType.Receptions,
        "player_anytime_td" => PredictionMarketType.AnytimeTouchdown,
        "player_pass_tds" => PredictionMarketType.PassingTouchdowns,
        _ => null
    };

    private static IReadOnlyList<PropLine> Deduplicate(List<PropLine> lines) =>
        lines
            .GroupBy(l => $"{l.Event.EventId}|{l.Market}|{l.PlayerName}|{l.TeamName}|{l.Line}")
            .Select(g => g.OrderByDescending(x => x.UpdatedAt).First())
            .ToList();

    private sealed class OddsEventDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("home_team")]
        public string? HomeTeam { get; set; }

        [JsonPropertyName("away_team")]
        public string? AwayTeam { get; set; }

        [JsonPropertyName("commence_time")]
        public DateTimeOffset CommenceTime { get; set; }

        [JsonPropertyName("bookmakers")]
        public List<OddsBookmakerDto>? Bookmakers { get; set; }
    }

    private sealed class OddsBookmakerDto
    {
        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("last_update")]
        public DateTimeOffset? LastUpdate { get; set; }

        [JsonPropertyName("markets")]
        public List<OddsMarketDto>? Markets { get; set; }
    }

    private sealed class OddsMarketDto
    {
        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("last_update")]
        public DateTimeOffset? LastUpdate { get; set; }

        [JsonPropertyName("outcomes")]
        public List<OddsOutcomeDto>? Outcomes { get; set; }
    }

    private sealed class OddsOutcomeDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("price")]
        public int Price { get; set; }

        [JsonPropertyName("point")]
        public double? Point { get; set; }
    }
}
