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
/// The Odds API — primary live NFL game/player prop lines.
/// Requires PropLines:OddsApi:ApiKey (or PropLines__OddsApi__ApiKey).
/// Quick Picks depends on <see cref="IPropLineProvider"/>, not this type directly.
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
                "PropLines__OddsApi__ApiKey is empty. Get a key at https://the-odds-api.com/ " +
                "or set PropLines:Provider=Mock for local development.");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var watch = Stopwatch.StartNew();
        var sport = Uri.EscapeDataString(_options.OddsApi.SportKey);
        var url =
            $"sports/{sport}/odds?apiKey={Uri.EscapeDataString(_options.OddsApi.ApiKey)}" +
            $"&regions={Uri.EscapeDataString(_options.OddsApi.Regions)}" +
            $"&markets={Uri.EscapeDataString(_options.OddsApi.GameMarkets)}" +
            "&oddsFormat=american";

        var events = await GetJsonAsync<List<OddsEventDto>>(client, url, cancellationToken)
            .ConfigureAwait(false) ?? [];

        var players = _players.GetAllPlayers();
        var lines = new List<PropLine>();
        var staleAfter = TimeSpan.FromMinutes(Math.Clamp(_options.StaleAfterMinutes, 15, 24 * 60));
        var maxEvents = Math.Clamp(_options.MaxEvents, 1, 16);
        var preferred = ParsePreferredBookmakers(_options.OddsApi.PreferredBookmakers);

        foreach (var ev in events.OrderBy(e => e.CommenceTime).Take(maxEvents))
        {
            if (string.IsNullOrWhiteSpace(ev.Id))
            {
                continue;
            }

            var footballEvent = new FootballEvent
            {
                EventId = ev.Id!,
                HomeTeam = AbbreviateTeam(ev.HomeTeam) ?? ev.HomeTeam ?? "Home",
                AwayTeam = AbbreviateTeam(ev.AwayTeam) ?? ev.AwayTeam ?? "Away",
                CommenceTime = ev.CommenceTime
            };

            foreach (var book in OrderBookmakers(ev.Bookmakers, preferred))
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
                    var detail = await GetJsonAsync<OddsEventDto>(client, propUrl, cancellationToken)
                        .ConfigureAwait(false);
                    if (detail?.Bookmakers is { Count: > 0 })
                    {
                        var propCount = 0;
                        foreach (var book in OrderBookmakers(detail.Bookmakers, preferred))
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

    private static async Task<T?> GetJsonAsync<T>(
        HttpClient client,
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var snippet = body.Length > 180 ? body[..180] + "…" : body;
            throw new HttpRequestException(
                $"The Odds API returned {(int)response.StatusCode} {response.ReasonPhrase}. {snippet}".Trim());
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static IEnumerable<OddsBookmakerDto> OrderBookmakers(
        List<OddsBookmakerDto>? books,
        HashSet<string> preferred)
    {
        if (books is null || books.Count == 0)
        {
            return [];
        }

        return books
            .OrderBy(b =>
            {
                var key = b.Key ?? string.Empty;
                return preferred.Contains(key) ? 0 : 1;
            })
            .ThenBy(b => b.Title ?? b.Key ?? string.Empty);
    }

    private static HashSet<string> ParsePreferredBookmakers(string? csv) =>
        (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<PropLine> MapGameMarket(
        FootballEvent ev,
        OddsBookmakerDto book,
        OddsMarketDto market,
        TimeSpan staleAfter)
    {
        var key = market.Key ?? string.Empty;
        var updated = market.LastUpdate ?? book.LastUpdate ?? DateTimeOffset.UtcNow;
        var freshness = ResolveFreshness(updated, staleAfter);

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
                NamesMatchTeam(o.Name, ev.HomeTeam));
            // Odds API uses full team names; also match against abbreviated event labels.
            home ??= market.Outcomes?.FirstOrDefault(o =>
                AbbreviateTeam(o.Name) == ev.HomeTeam);
            if (home?.Point is null && market.Outcomes is { Count: > 0 })
            {
                // Fall back to first outcome with a point when name matching fails.
                home = market.Outcomes.FirstOrDefault(o => o.Point is not null);
            }

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
            var home = market.Outcomes?.FirstOrDefault(o => NamesMatchTeam(o.Name, ev.HomeTeam))
                       ?? market.Outcomes?.FirstOrDefault(o => AbbreviateTeam(o.Name) == ev.HomeTeam);
            var away = market.Outcomes?.FirstOrDefault(o => NamesMatchTeam(o.Name, ev.AwayTeam))
                       ?? market.Outcomes?.FirstOrDefault(o => AbbreviateTeam(o.Name) == ev.AwayTeam);

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
                AmericanOddsUnder = away?.Price
            };
        }
        else if (key is "team_totals")
        {
            var over = market.Outcomes?.FirstOrDefault(o =>
                string.Equals(o.Name, "Over", StringComparison.OrdinalIgnoreCase));
            if (over?.Point is null)
            {
                yield break;
            }

            yield return new PropLine
            {
                Id = $"{ev.EventId}:team_totals:{book.Key}:{over.Description}",
                Event = ev,
                TeamName = AbbreviateTeam(over.Description) ?? over.Description,
                Market = PredictionMarketType.TeamTotal,
                Line = (decimal)over.Point.Value,
                Bookmaker = book.Title ?? book.Key ?? "book",
                Source = "TheOddsAPI",
                UpdatedAt = updated,
                Freshness = freshness,
                AmericanOddsOver = over.Price,
                AmericanOddsUnder = market.Outcomes?
                    .FirstOrDefault(o =>
                        string.Equals(o.Name, "Under", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(o.Description, over.Description, StringComparison.OrdinalIgnoreCase))
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
        var freshness = ResolveFreshness(updated, staleAfter);

        var overs = (market.Outcomes ?? [])
            .Where(o =>
                string.Equals(o.Name, "Over", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(o.Name, "Yes", StringComparison.OrdinalIgnoreCase) ||
                (marketType == PredictionMarketType.AnytimeTouchdown &&
                 !string.IsNullOrWhiteSpace(o.Description)))
            .GroupBy(o => o.Description ?? o.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        foreach (var outcome in overs)
        {
            var playerName = outcome.Description;
            if (string.IsNullOrWhiteSpace(playerName) ||
                string.Equals(playerName, "Over", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(playerName, "Yes", StringComparison.OrdinalIgnoreCase))
            {
                playerName = outcome.Description;
            }

            if (string.IsNullOrWhiteSpace(playerName) ||
                string.Equals(playerName, "Over", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(playerName, "Under", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(playerName, "Yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(playerName, "No", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var player = MatchPlayer(players, playerName);

            decimal? line = outcome.Point is double pt ? (decimal)pt : null;
            if (marketType == PredictionMarketType.AnytimeTouchdown)
            {
                line ??= 0.5m;
            }

            if (line is null && marketType != PredictionMarketType.AnytimeTouchdown)
            {
                continue;
            }

            var under = (market.Outcomes ?? []).FirstOrDefault(o =>
                string.Equals(o.Description, playerName, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(o.Name, "Under", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(o.Name, "No", StringComparison.OrdinalIgnoreCase)));

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
                AmericanOddsOver = outcome.Price,
                AmericanOddsUnder = under?.Price
            };
        }
    }

    private static PropLineFreshness ResolveFreshness(DateTimeOffset updated, TimeSpan staleAfter)
    {
        // Never label outdated book timestamps as Live.
        return DateTimeOffset.UtcNow - updated > staleAfter
            ? PropLineFreshness.Stale
            : PropLineFreshness.Live;
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

    private static Core.Players.Player? MatchPlayer(
        IReadOnlyList<Core.Players.Player> players,
        string playerName)
    {
        var exact = players.FirstOrDefault(p =>
            string.Equals(p.FullName, playerName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var normTarget = NormalizePersonName(playerName);
        var byNormalized = players.FirstOrDefault(p =>
            NormalizePersonName(p.FullName) == normTarget);
        if (byNormalized is not null)
        {
            return byNormalized;
        }

        // Last-name + first-initial (common Odds API / injury report style).
        var parts = normTarget.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var last = parts[^1];
            var firstInitial = parts[0][0];
            var candidates = players
                .Where(p =>
                {
                    var pn = NormalizePersonName(p.FullName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    return pn.Length >= 2 &&
                           pn[^1] == last &&
                           pn[0].Length > 0 &&
                           pn[0][0] == firstInitial;
                })
                .ToList();
            if (candidates.Count == 1)
            {
                return candidates[0];
            }
        }

        return players.FirstOrDefault(p =>
            NormalizePersonName(p.FullName).EndsWith(normTarget, StringComparison.Ordinal) ||
            normTarget.EndsWith(NormalizePersonName(p.FullName), StringComparison.Ordinal));
    }

    private static string NormalizePersonName(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Replace(".", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal)
            .Replace("’", "", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Where(c => char.IsLetter(c) || char.IsWhiteSpace(c))
            .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool NamesMatchTeam(string? outcomeName, string teamLabel) =>
        !string.IsNullOrWhiteSpace(outcomeName) &&
        (string.Equals(outcomeName, teamLabel, StringComparison.OrdinalIgnoreCase) ||
         outcomeName.Contains(teamLabel, StringComparison.OrdinalIgnoreCase) ||
         teamLabel.Contains(outcomeName, StringComparison.OrdinalIgnoreCase));

    private static string? AbbreviateTeam(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (name.Length <= 3 && name.All(char.IsLetter))
        {
            return name.ToUpperInvariant();
        }

        return name.Trim().ToLowerInvariant() switch
        {
            "arizona cardinals" => "ARI",
            "atlanta falcons" => "ATL",
            "baltimore ravens" => "BAL",
            "buffalo bills" => "BUF",
            "carolina panthers" => "CAR",
            "chicago bears" => "CHI",
            "cincinnati bengals" => "CIN",
            "cleveland browns" => "CLE",
            "dallas cowboys" => "DAL",
            "denver broncos" => "DEN",
            "detroit lions" => "DET",
            "green bay packers" => "GB",
            "houston texans" => "HOU",
            "indianapolis colts" => "IND",
            "jacksonville jaguars" => "JAX",
            "kansas city chiefs" => "KC",
            "las vegas raiders" => "LV",
            "los angeles chargers" => "LAC",
            "los angeles rams" => "LAR",
            "miami dolphins" => "MIA",
            "minnesota vikings" => "MIN",
            "new england patriots" => "NE",
            "new orleans saints" => "NO",
            "new york giants" => "NYG",
            "new york jets" => "NYJ",
            "philadelphia eagles" => "PHI",
            "pittsburgh steelers" => "PIT",
            "san francisco 49ers" => "SF",
            "seattle seahawks" => "SEA",
            "tampa bay buccaneers" => "TB",
            "tennessee titans" => "TEN",
            "washington commanders" => "WAS",
            _ => null
        };
    }

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
