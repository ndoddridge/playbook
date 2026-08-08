using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;
using Playbook.Infrastructure.Players;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Live NFL season statistics from Sleeper bulk season endpoints.
/// College box scores are not supplied by Sleeper — those records are omitted (never fabricated).
/// </summary>
public sealed class LivePlayerStatsProvider : IPlayerStatsProvider
{
    public const string HttpClientName = "SleeperPlayerStats";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LivePlayerStatsProvider> _logger;

    public LivePlayerStatsProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<LivePlayerStatsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public PlayerStatsProviderKind Kind => PlayerStatsProviderKind.Live;

    public string DisplayName => "Live (Sleeper Stats)";

    public async Task<IReadOnlyList<PlayerSeasonStats>> GetSeasonStatsAsync(
        PlayerStatsSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var rows = new List<PlayerSeasonStats>();
        var now = DateTimeOffset.UtcNow;

        foreach (var season in request.CompletedSeasons.Distinct().OrderBy(s => s))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seasonRows = await FetchSeasonAsync(
                    client,
                    season,
                    request.SeasonType,
                    StatsPeriod.CompletedSeason,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
            rows.AddRange(seasonRows);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var currentRows = await FetchSeasonAsync(
                client,
                request.CurrentSeason,
                request.SeasonType,
                StatsPeriod.CurrentSeason,
                now,
                cancellationToken)
            .ConfigureAwait(false);
        rows.AddRange(currentRows);

        watch.Stop();
        _logger.LogInformation(
            "Sleeper stats loaded {Count} season records across {Seasons} seasons in {Ms} ms",
            rows.Count,
            request.CompletedSeasons.Count + 1,
            watch.ElapsedMilliseconds);

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("Sleeper returned no player season statistics.");
        }

        return rows;
    }

    private async Task<IReadOnlyList<PlayerSeasonStats>> FetchSeasonAsync(
        HttpClient client,
        int season,
        string seasonType,
        StatsPeriod period,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var path = $"stats/nfl/regular/{season}";
        using var response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Sleeper stats season {Season} returned {Status}",
                season,
                (int)response.StatusCode);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<Dictionary<string, SleeperStatRow>>(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (payload is null || payload.Count == 0)
        {
            return [];
        }

        var rows = new List<PlayerSeasonStats>(payload.Count);
        foreach (var (sleeperId, dto) in payload)
        {
            var mapped = MapRow(sleeperId, dto, season, seasonType, period, now);
            if (mapped is null)
            {
                continue;
            }

            rows.Add(mapped);
        }

        return rows;
    }

    private static PlayerSeasonStats? MapRow(
        string sleeperId,
        SleeperStatRow dto,
        int season,
        string seasonType,
        StatsPeriod period,
        DateTimeOffset now)
    {
        var games = AsInt(dto.Gp) ?? AsInt(dto.GmsActive);
        var stats = new PlayerSeasonStats
        {
            PlayerId = SleeperPlayerIds.ToPlaybookId(sleeperId),
            Season = season,
            SeasonType = string.IsNullOrWhiteSpace(seasonType) ? "regular" : seasonType,
            Period = period,
            Level = FootballLevel.Nfl,
            Games = games,
            Starts = AsInt(dto.Gs),
            PassAttempts = AsInt(dto.PassAtt),
            PassCompletions = AsInt(dto.PassCmp),
            PassYards = AsInt(dto.PassYd),
            PassTouchdowns = AsInt(dto.PassTd),
            PassInterceptions = AsInt(dto.PassInt),
            RushAttempts = AsInt(dto.RushAtt),
            RushYards = AsInt(dto.RushYd),
            RushTouchdowns = AsInt(dto.RushTd),
            Targets = AsInt(dto.RecTgt),
            Receptions = AsInt(dto.Rec),
            ReceivingYards = AsInt(dto.RecYd),
            ReceivingTouchdowns = AsInt(dto.RecTd),
            Fumbles = AsInt(dto.Fum),
            FantasyPointsStandard = AsDecimal(dto.PtsStd),
            FantasyPointsHalfPpr = AsDecimal(dto.PtsHalfPpr),
            FantasyPointsPpr = AsDecimal(dto.PtsPpr),
            SourceProvider = "Sleeper",
            Source = $"stats/nfl/regular/{season}",
            IdentityMatch = StatsIdentityMatch.Matched,
            LastUpdated = now
        };

        return stats.HasAnyCountingStat ? stats : null;
    }

    private static int? AsInt(double? value) =>
        value is null ? null : (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);

    private static decimal? AsDecimal(double? value) =>
        value is null ? null : Math.Round((decimal)value.Value, 2, MidpointRounding.AwayFromZero);

    private sealed class SleeperStatRow
    {
        [JsonPropertyName("gp")]
        public double? Gp { get; set; }

        [JsonPropertyName("gms_active")]
        public double? GmsActive { get; set; }

        [JsonPropertyName("gs")]
        public double? Gs { get; set; }

        [JsonPropertyName("pass_att")]
        public double? PassAtt { get; set; }

        [JsonPropertyName("pass_cmp")]
        public double? PassCmp { get; set; }

        [JsonPropertyName("pass_yd")]
        public double? PassYd { get; set; }

        [JsonPropertyName("pass_td")]
        public double? PassTd { get; set; }

        [JsonPropertyName("pass_int")]
        public double? PassInt { get; set; }

        [JsonPropertyName("rush_att")]
        public double? RushAtt { get; set; }

        [JsonPropertyName("rush_yd")]
        public double? RushYd { get; set; }

        [JsonPropertyName("rush_td")]
        public double? RushTd { get; set; }

        [JsonPropertyName("rec_tgt")]
        public double? RecTgt { get; set; }

        [JsonPropertyName("rec")]
        public double? Rec { get; set; }

        [JsonPropertyName("rec_yd")]
        public double? RecYd { get; set; }

        [JsonPropertyName("rec_td")]
        public double? RecTd { get; set; }

        [JsonPropertyName("pts_std")]
        public double? PtsStd { get; set; }

        [JsonPropertyName("pts_half_ppr")]
        public double? PtsHalfPpr { get; set; }

        [JsonPropertyName("pts_ppr")]
        public double? PtsPpr { get; set; }

        [JsonPropertyName("fum")]
        public double? Fum { get; set; }
    }
}
