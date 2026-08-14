using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Playbook.Application.Players;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Real preseason player box scores from ESPN's public scoreboard/summary API — the only real
/// source of preseason production Playbook has (nflverse's player_stats feed carries REG/POST
/// only, confirmed empty of PRE rows). Resolves ESPN athlete ids to Playbook player ids via the
/// existing <see cref="IPlayerIdentityDirectory"/> crosswalk (same one Sleeper populates
/// <c>EspnId</c> into) — no new identity system. Results are cached per (season, Eastern game
/// date) for the lifetime of the process since a completed game's box score never changes.
/// </summary>
public sealed class EspnPreseasonGameLogProvider : IPreseasonPlayerGameLogProvider
{
    public const string HttpClientName = "EspnPreseasonBoxScores";

    private static readonly TimeZoneInfo Eastern = ResolveEasternTimeZone();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPlayerIdentityDirectory _identities;
    private readonly ILogger<EspnPreseasonGameLogProvider> _logger;
    private readonly ConcurrentDictionary<(int Season, DateOnly Date), IReadOnlyList<PlayerGameStats>> _cache = new();

    public EspnPreseasonGameLogProvider(
        IHttpClientFactory httpClientFactory,
        IPlayerIdentityDirectory identities,
        ILogger<EspnPreseasonGameLogProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _identities = identities;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlayerGameStats>> GetPreseasonGameLogsAsync(
        int season,
        DateTimeOffset gameDate,
        CancellationToken cancellationToken = default)
    {
        var easternDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(gameDate, Eastern).DateTime);
        var key = (season, easternDate);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        IReadOnlyList<PlayerGameStats> result;
        try
        {
            result = await FetchAsync(season, easternDate, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ESPN preseason boxscore lookup failed for {Season} {Date}", season, easternDate);
            return [];
        }

        _cache[key] = result;
        return result;
    }

    private async Task<IReadOnlyList<PlayerGameStats>> FetchAsync(
        int season, DateOnly easternDate, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var dateParam = easternDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scoreboard = await client
            .GetFromJsonAsync<EspnScoreboardResponse>(
                $"sports/football/nfl/scoreboard?seasontype=1&dates={dateParam}", cancellationToken)
            .ConfigureAwait(false);

        var events = scoreboard?.Events ?? [];
        var finished = events
            .Where(e => e.Id is not null &&
                        e.Competitions?.FirstOrDefault()?.Status?.Type?.Completed == true)
            .ToList();

        if (finished.Count == 0)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        var results = new List<PlayerGameStats>();
        foreach (var ev in finished)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var summary = await client
                .GetFromJsonAsync<EspnSummaryResponse>($"sports/football/nfl/summary?event={ev.Id}", cancellationToken)
                .ConfigureAwait(false);

            var teams = summary?.Boxscore?.Players ?? [];
            foreach (var team in teams)
            {
                results.AddRange(MapTeamBoxScore(team, season, ev, now));
            }
        }

        return results;
    }

    private IEnumerable<PlayerGameStats> MapTeamBoxScore(
        EspnTeamPlayersDto team, int season, EspnScoreboardEventDto ev, DateTimeOffset now)
    {
        var accumulators = new Dictionary<string, PreseasonAccumulator>(StringComparer.Ordinal);

        foreach (var category in team.Statistics ?? [])
        {
            if (category.Name is not ("passing" or "rushing" or "receiving"))
            {
                continue;
            }

            var keys = category.Keys ?? [];
            foreach (var athleteStat in category.Athletes ?? [])
            {
                var espnId = athleteStat.Athlete?.Id;
                if (string.IsNullOrWhiteSpace(espnId))
                {
                    continue;
                }

                var identity = _identities.GetByEspnId(espnId);
                if (identity is null)
                {
                    // No crosswalk to a Playbook player — never guess an identity.
                    continue;
                }

                if (!accumulators.TryGetValue(espnId, out var acc))
                {
                    acc = new PreseasonAccumulator(identity.PlaybookId);
                    accumulators[espnId] = acc;
                }

                ApplyCategory(acc, category.Name!, keys, athleteStat.Stats ?? []);
            }
        }

        var week = ev.Week?.Number ?? 0;
        var eventSeason = ev.Season?.Year ?? season;
        foreach (var acc in accumulators.Values)
        {
            yield return acc.ToGameStats(eventSeason, week, now);
        }
    }

    private static void ApplyCategory(
        PreseasonAccumulator acc, string categoryName, IReadOnlyList<string> keys, IReadOnlyList<string> stats)
    {
        for (var i = 0; i < keys.Count && i < stats.Count; i++)
        {
            var key = keys[i];
            var raw = stats[i];

            if (key == "completions/passingAttempts")
            {
                var parts = raw.Split('/', 2);
                if (parts.Length == 2)
                {
                    acc.PassCompletions = ParseInt(parts[0]);
                    acc.PassAttempts = ParseInt(parts[1]);
                }

                continue;
            }

            switch (key)
            {
                case "passingYards": acc.PassYards = ParseInt(raw); break;
                case "passingTouchdowns": acc.PassTouchdowns = ParseInt(raw); break;
                case "interceptions" when categoryName == "passing": acc.PassInterceptions = ParseInt(raw); break;
                case "rushingAttempts": acc.RushAttempts = ParseInt(raw); break;
                case "rushingYards": acc.RushYards = ParseInt(raw); break;
                case "rushingTouchdowns": acc.RushTouchdowns = ParseInt(raw); break;
                case "receptions": acc.Receptions = ParseInt(raw); break;
                case "receivingYards": acc.ReceivingYards = ParseInt(raw); break;
                case "receivingTouchdowns": acc.ReceivingTouchdowns = ParseInt(raw); break;
            }
        }
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    private sealed class PreseasonAccumulator(Guid playerId)
    {
        public Guid PlayerId { get; } = playerId;
        public int? PassAttempts, PassCompletions, PassYards, PassTouchdowns, PassInterceptions;
        public int? RushAttempts, RushYards, RushTouchdowns;
        public int? Receptions, ReceivingYards, ReceivingTouchdowns;

        public PlayerGameStats ToGameStats(int season, int week, DateTimeOffset now) => new()
        {
            PlayerId = PlayerId,
            Season = season,
            Week = week,
            SeasonType = "preseason",
            Level = FootballLevel.Nfl,
            PassAttempts = PassAttempts,
            PassCompletions = PassCompletions,
            PassYards = PassYards,
            PassTouchdowns = PassTouchdowns,
            PassInterceptions = PassInterceptions,
            RushAttempts = RushAttempts,
            RushYards = RushYards,
            RushTouchdowns = RushTouchdowns,
            Receptions = Receptions,
            ReceivingYards = ReceivingYards,
            ReceivingTouchdowns = ReceivingTouchdowns,
            SourceProvider = "ESPN",
            Source = "espn-boxscore-preseason",
            IdentityMatch = StatsIdentityMatch.Matched,
            LastUpdated = now
        };
    }

    private sealed class EspnScoreboardResponse
    {
        [JsonPropertyName("events")]
        public List<EspnScoreboardEventDto>? Events { get; set; }
    }

    private sealed class EspnScoreboardEventDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("season")]
        public EspnSeasonRefDto? Season { get; set; }

        [JsonPropertyName("week")]
        public EspnWeekRefDto? Week { get; set; }

        [JsonPropertyName("competitions")]
        public List<EspnCompetitionDto>? Competitions { get; set; }
    }

    private sealed class EspnSeasonRefDto
    {
        [JsonPropertyName("year")]
        public int? Year { get; set; }
    }

    private sealed class EspnWeekRefDto
    {
        [JsonPropertyName("number")]
        public int? Number { get; set; }
    }

    private sealed class EspnCompetitionDto
    {
        [JsonPropertyName("status")]
        public EspnStatusDto? Status { get; set; }
    }

    private sealed class EspnStatusDto
    {
        [JsonPropertyName("type")]
        public EspnStatusTypeDto? Type { get; set; }
    }

    private sealed class EspnStatusTypeDto
    {
        [JsonPropertyName("completed")]
        public bool Completed { get; set; }
    }

    private sealed class EspnSummaryResponse
    {
        [JsonPropertyName("boxscore")]
        public EspnBoxscoreDto? Boxscore { get; set; }
    }

    private sealed class EspnBoxscoreDto
    {
        [JsonPropertyName("players")]
        public List<EspnTeamPlayersDto>? Players { get; set; }
    }

    private sealed class EspnTeamPlayersDto
    {
        [JsonPropertyName("statistics")]
        public List<EspnStatCategoryDto>? Statistics { get; set; }
    }

    private sealed class EspnStatCategoryDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("keys")]
        public List<string>? Keys { get; set; }

        [JsonPropertyName("athletes")]
        public List<EspnAthleteStatDto>? Athletes { get; set; }
    }

    private sealed class EspnAthleteStatDto
    {
        [JsonPropertyName("athlete")]
        public EspnAthleteRefDto? Athlete { get; set; }

        [JsonPropertyName("stats")]
        public List<string>? Stats { get; set; }
    }

    private sealed class EspnAthleteRefDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }
}
