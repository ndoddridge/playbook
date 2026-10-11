using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Players;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// NFL game lines and player props from ESPN's public APIs, with no API key. ESPN carries its
/// sportsbook partner's lines (DraftKings at the time of writing):
/// <list type="bullet">
/// <item>The scoreboard (this week and next) gives each game's spread, total and moneyline, with prices.</item>
/// <item>The core API's prop bets give each game's player lines: passing, rushing and receiving yards,
/// receptions and passing touchdowns, plus who is offered as an anytime touchdown scorer. ESPN
/// publishes the line but not its price, so <see cref="PropLine.AmericanOddsOver"/> stays empty for props.</item>
/// </list>
/// Prop athletes are ESPN ids, matched to Playbook players through the Sleeper ESPN-id crosswalk.
/// </summary>
public sealed partial class EspnPropLineProvider : IPropLineProvider
{
    public const string HttpClientName = "EspnOdds";
    public const string Name = "ESPN";

    private const string ScoreboardUrl = "https://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard";
    private const string CoreUrl = "https://sports.core.api.espn.com/v2/sports/football/leagues/nfl/";
    private const int MaxPropPages = 3;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>ESPN prop types Playbook models (full-game lines only), in display priority order.</summary>
    internal static readonly IReadOnlyDictionary<string, PredictionMarketType> PropTypes =
        new Dictionary<string, PredictionMarketType>(StringComparer.OrdinalIgnoreCase)
        {
            ["Total Passing Yards (incl. overtime)"] = PredictionMarketType.PassingYards,
            ["Total Rushing Yards (incl. overtime)"] = PredictionMarketType.RushingYards,
            ["Total Receiving Yards (incl. overtime)"] = PredictionMarketType.ReceivingYards,
            ["Total Receptions (incl. overtime)"] = PredictionMarketType.Receptions,
            ["Total Passing Touchdowns (incl. overtime)"] = PredictionMarketType.PassingTouchdowns,
            ["Anytime Touchdown Scorer"] = PredictionMarketType.AnytimeTouchdown,
        };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPlayerService _players;
    private readonly IPlayerIdentityDirectory _identities;
    private readonly PropLineOptions _options;
    private readonly ILogger<EspnPropLineProvider> _logger;

    public EspnPropLineProvider(
        IHttpClientFactory httpClientFactory,
        IPlayerService players,
        IPlayerIdentityDirectory identities,
        IOptions<PropLineOptions> options,
        ILogger<EspnPropLineProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _players = players;
        _identities = identities;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => Name;

    public async Task<IReadOnlyList<PropLine>> GetPropLinesAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var watch = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow;

        var current = await GetJsonAsync<ScoreboardDto>(client, ScoreboardUrl, cancellationToken).ConfigureAwait(false);
        var season = current?.Season?.Year ?? 0;
        var events = (current?.Events ?? []).Select(e => (Event: e, Week: current?.Week?.Number ?? 0)).ToList();
        if (current?.Week?.Number is int week && current.Season?.Type is int seasonType && current.Season.Year is int year)
        {
            try
            {
                var next = await GetJsonAsync<ScoreboardDto>(
                        client,
                        $"{ScoreboardUrl}?seasontype={seasonType}&week={week + 1}&dates={year}",
                        cancellationToken)
                    .ConfigureAwait(false);
                events.AddRange((next?.Events ?? []).Select(e => (Event: e, Week: week + 1)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "ESPN scoreboard for the following week is unavailable");
            }
        }

        var phase = ParsePhase(current?.Season?.Type);
        var upcoming = events
            .Where(e => !string.IsNullOrWhiteSpace(e.Event.Id) && e.Event.Status?.Type?.Completed != true)
            .GroupBy(e => e.Event.Id!)
            .Select(g => g.First())
            .OrderBy(e => e.Event.Kickoff)
            .Take(Math.Clamp(_options.MaxEvents, 1, 40))
            .ToList();

        var lines = new List<PropLine>();
        foreach (var (ev, eventWeek) in upcoming)
        {
            var footballEvent = ToFootballEvent(ev, phase, season, eventWeek);
            if (footballEvent is null)
            {
                continue;
            }

            var odds = ev.Competitions?.FirstOrDefault()?.Odds?.FirstOrDefault();
            if (odds is not null)
            {
                lines.AddRange(MapGameLines(footballEvent, odds, now));
            }

            if (!_options.OddsApi.FetchPlayerProps)
            {
                continue;
            }

            try
            {
                var props = await GetPropBetsAsync(client, ev.Id!, odds?.Provider?.Id ?? "100", cancellationToken)
                    .ConfigureAwait(false);
                lines.AddRange(MapPlayerProps(footballEvent, props, odds?.Provider?.Name, now, ResolvePlayer)
                    .Take(Math.Max(1, _options.MaxPlayerPropsPerEvent)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "ESPN prop bets failed for event {EventId}", ev.Id);
            }
        }

        watch.Stop();
        _logger.LogInformation(
            "ESPN loaded {Count} lines from {Events} upcoming games in {Ms} ms",
            lines.Count,
            upcoming.Count,
            watch.ElapsedMilliseconds);
        return lines;
    }

    private Core.Players.Player? ResolvePlayer(string espnId)
    {
        var identity = _identities.GetByEspnId(espnId);
        return identity is null ? null : _players.GetPlayer(identity.PlaybookId);
    }

    private static async Task<List<PropBetDto>> GetPropBetsAsync(
        HttpClient client,
        string eventId,
        string providerId,
        CancellationToken cancellationToken)
    {
        var id = Uri.EscapeDataString(eventId);
        var items = new List<PropBetDto>();
        for (var page = 1; page <= MaxPropPages; page++)
        {
            var url = $"{CoreUrl}events/{id}/competitions/{id}/odds/{Uri.EscapeDataString(providerId)}/propBets" +
                      $"?lang=en&region=us&limit=1000&page={page}";
            // ESPN answers 404 until the book has posted props for a game (next week's, usually).
            var result = await GetJsonAsync<PropBetPageDto>(client, url, cancellationToken, notFoundIsEmpty: true)
                .ConfigureAwait(false);
            items.AddRange(result?.Items ?? []);
            if (result is null || page >= (result.PageCount ?? 1))
            {
                break;
            }
        }

        return items;
    }

    private static async Task<T?> GetJsonAsync<T>(
        HttpClient client,
        string url,
        CancellationToken cancellationToken,
        bool notFoundIsEmpty = false)
    {
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (notFoundIsEmpty && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"ESPN returned {(int)response.StatusCode} {response.ReasonPhrase} for {url}");
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    internal static NflSeasonPhase ParsePhase(int? seasonType) => seasonType switch
    {
        1 => NflSeasonPhase.Preseason,
        3 => NflSeasonPhase.Postseason,
        _ => NflSeasonPhase.RegularSeason,
    };

    /// <summary>ESPN states each game's season and week, so they are set here rather than inferred from kickoffs.</summary>
    internal static FootballEvent? ToFootballEvent(EventDto ev, NflSeasonPhase phase, int season, int week)
    {
        var competitors = ev.Competitions?.FirstOrDefault()?.Competitors ?? [];
        var home = competitors.FirstOrDefault(c => string.Equals(c.HomeAway, "home", StringComparison.OrdinalIgnoreCase));
        var away = competitors.FirstOrDefault(c => string.Equals(c.HomeAway, "away", StringComparison.OrdinalIgnoreCase));
        if (home?.Team?.Abbreviation is null || away?.Team?.Abbreviation is null || ev.Kickoff is null)
        {
            return null;
        }

        return new FootballEvent
        {
            EventId = ev.Id!,
            HomeTeam = NormalizeTeam(home.Team.Abbreviation),
            AwayTeam = NormalizeTeam(away.Team.Abbreviation),
            CommenceTime = ev.Kickoff.Value,
            PhaseHint = phase,
            Phase = phase,
            Season = season,
            Week = week,
        };
    }

    /// <summary>Playbook (and Sleeper) write Washington as WAS; ESPN writes WSH.</summary>
    internal static string NormalizeTeam(string abbreviation) =>
        abbreviation.ToUpperInvariant() switch
        {
            "WSH" => "WAS",
            var other => other,
        };

    internal static IEnumerable<PropLine> MapGameLines(FootballEvent ev, OddsDto odds, DateTimeOffset now)
    {
        var book = odds.Provider?.Name ?? "ESPN";

        var total = odds.OverUnder ?? ParseLine(odds.Total?.Over?.Close?.Line);
        if (total is not null)
        {
            yield return NewGameLine(ev, "totals", book, now, PredictionMarketType.GameTotal, null, total,
                ParseAmerican(odds.Total?.Over?.Close?.Odds), ParseAmerican(odds.Total?.Under?.Close?.Odds));
        }

        var spread = ParseLine(odds.PointSpread?.Home?.Close?.Line) ?? odds.Spread;
        if (spread is not null)
        {
            yield return NewGameLine(ev, "spreads", book, now, PredictionMarketType.Spread, ev.HomeTeam, spread,
                ParseAmerican(odds.PointSpread?.Home?.Close?.Odds), null);
        }

        var homeMoneyline = ParseAmerican(odds.Moneyline?.Home?.Close?.Odds);
        var awayMoneyline = ParseAmerican(odds.Moneyline?.Away?.Close?.Odds);
        if (homeMoneyline is not null || awayMoneyline is not null)
        {
            yield return NewGameLine(ev, "h2h", book, now, PredictionMarketType.Winner, ev.HomeTeam, null,
                homeMoneyline, awayMoneyline);
        }
    }

    private static PropLine NewGameLine(
        FootballEvent ev,
        string key,
        string book,
        DateTimeOffset now,
        PredictionMarketType market,
        string? team,
        decimal? line,
        int? over,
        int? under) => new()
    {
        Id = $"{ev.EventId}:{key}:espn",
        Event = ev,
        TeamName = team,
        Market = market,
        Line = line,
        Bookmaker = book,
        Source = Name,
        UpdatedAt = now,
        Freshness = PropLineFreshness.Live,
        AmericanOddsOver = over,
        AmericanOddsUnder = under,
    };

    /// <summary>
    /// One line per player and market. ESPN lists each line twice (its over and its under side); where a
    /// player has more than one value, the one listed most often is the main line.
    /// </summary>
    internal static IEnumerable<PropLine> MapPlayerProps(
        FootballEvent ev,
        IEnumerable<PropBetDto> items,
        string? bookmaker,
        DateTimeOffset now,
        Func<string, Core.Players.Player?> resolvePlayer)
    {
        var book = bookmaker ?? "ESPN";
        var grouped = items
            .Where(i => i.Type?.Name is not null && PropTypes.ContainsKey(i.Type.Name))
            .Select(i => (Item: i, AthleteId: AthleteId(i.Athlete?.Ref), Market: PropTypes[i.Type!.Name!]))
            .Where(x => x.AthleteId is not null)
            .GroupBy(x => (x.AthleteId, x.Market))
            .OrderBy(g => g.Key.Market == PredictionMarketType.AnytimeTouchdown ? 1 : 0)
            .ThenBy(g => g.Key.Market);

        foreach (var group in grouped)
        {
            var player = resolvePlayer(group.Key.AthleteId!);
            if (player is null)
            {
                continue;
            }

            decimal? line;
            if (group.Key.Market == PredictionMarketType.AnytimeTouchdown)
            {
                line = 0.5m;
            }
            else
            {
                line = group
                    .Select(x => x.Item.Current?.Target?.Value)
                    .Where(v => v is not null)
                    .GroupBy(v => v!.Value)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key)
                    .Select(g => (decimal?)g.Key)
                    .FirstOrDefault();
                if (line is null)
                {
                    continue;
                }
            }

            yield return new PropLine
            {
                Id = $"{ev.EventId}:{group.Key.Market}:espn:{group.Key.AthleteId}",
                Event = ev,
                PlayerId = player.Id,
                PlayerName = player.FullName,
                TeamName = player.Team,
                Market = group.Key.Market,
                Line = line,
                Bookmaker = book,
                Source = Name,
                UpdatedAt = now,
                Freshness = PropLineFreshness.Live,
            };
        }
    }

    /// <summary>The ESPN athlete id in a core API athlete reference, e.g. ".../athletes/4040715?lang=en".</summary>
    internal static string? AthleteId(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var match = AthleteIdPattern().Match(reference);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"/athletes/(\d+)")]
    private static partial Regex AthleteIdPattern();

    /// <summary>"-7.5", "+3", "o41.5", "u41.5" → the number.</summary>
    internal static decimal? ParseLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim().TrimStart('o', 'u', 'O', 'U');
        return decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>"-395", "+310", "EVEN" → American odds.</summary>
    internal static int? ParseAmerican(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (string.Equals(trimmed, "EVEN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "EV", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        return int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    internal sealed class ScoreboardDto
    {
        public WeekDto? Week { get; set; }
        public SeasonDto? Season { get; set; }
        public List<EventDto>? Events { get; set; }
    }

    internal sealed class WeekDto
    {
        public int? Number { get; set; }
    }

    internal sealed class SeasonDto
    {
        public int? Type { get; set; }
        public int? Year { get; set; }
    }

    internal sealed class EventDto
    {
        public string? Id { get; set; }
        /// <summary>ESPN writes kickoff without seconds ("2026-10-11T13:30Z"); see <see cref="Kickoff"/>.</summary>
        public string? Date { get; set; }
        public StatusDto? Status { get; set; }

        public DateTimeOffset? Kickoff =>
            DateTimeOffset.TryParse(Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
                ? value
                : null;
        public List<CompetitionDto>? Competitions { get; set; }
    }

    internal sealed class StatusDto
    {
        public StatusTypeDto? Type { get; set; }
    }

    internal sealed class StatusTypeDto
    {
        public bool? Completed { get; set; }
    }

    internal sealed class CompetitionDto
    {
        public List<CompetitorDto>? Competitors { get; set; }
        public List<OddsDto>? Odds { get; set; }
    }

    internal sealed class CompetitorDto
    {
        public string? HomeAway { get; set; }
        public TeamDto? Team { get; set; }
    }

    internal sealed class TeamDto
    {
        public string? Abbreviation { get; set; }
    }

    internal sealed class OddsDto
    {
        public ProviderDto? Provider { get; set; }
        public decimal? OverUnder { get; set; }
        public decimal? Spread { get; set; }
        public SidesDto? Moneyline { get; set; }
        public SidesDto? PointSpread { get; set; }
        public TotalDto? Total { get; set; }
    }

    internal sealed class ProviderDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    internal sealed class SidesDto
    {
        public SideDto? Home { get; set; }
        public SideDto? Away { get; set; }
    }

    internal sealed class TotalDto
    {
        public SideDto? Over { get; set; }
        public SideDto? Under { get; set; }
    }

    internal sealed class SideDto
    {
        public PriceDto? Close { get; set; }
    }

    internal sealed class PriceDto
    {
        public string? Line { get; set; }
        public string? Odds { get; set; }
    }

    internal sealed class PropBetPageDto
    {
        public int? PageCount { get; set; }
        public List<PropBetDto>? Items { get; set; }
    }

    internal sealed class PropBetDto
    {
        public RefDto? Athlete { get; set; }
        public PropTypeDto? Type { get; set; }
        public PropValueDto? Current { get; set; }
    }

    internal sealed class RefDto
    {
        [JsonPropertyName("$ref")]
        public string? Ref { get; set; }
    }

    internal sealed class PropTypeDto
    {
        public string? Name { get; set; }
    }

    internal sealed class PropValueDto
    {
        public TargetDto? Target { get; set; }
    }

    internal sealed class TargetDto
    {
        public decimal? Value { get; set; }
    }
}
