using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Playbook.Application.Players.Data;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Players;

/// <summary>
/// Live NFL player catalog via the public Sleeper API (players, teams, positions, status).
/// Auth is configuration-ready (<see cref="SleeperOptions.ApiKey"/>) though unused for public reads.
/// </summary>
public sealed class LivePlayerDataProvider : IPlayerDataProvider
{
    public const string HttpClientName = "SleeperPlayerData";

    private static readonly string[] FantasyPositions = ["QB", "RB", "WR", "TE", "K", "DEF"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LivePlayerDataProvider> _logger;

    public LivePlayerDataProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<LivePlayerDataProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public PlayerDataProviderKind Kind => PlayerDataProviderKind.Live;

    public string DisplayName => "Live (Sleeper)";

    public async Task<IReadOnlyList<Player>> GetPlayersAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var byId = new Dictionary<string, Player>(StringComparer.Ordinal);
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        foreach (var position in FantasyPositions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = $"players/nfl?position={Uri.EscapeDataString(position)}&active=true";
            using var response = await httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content
                .ReadFromJsonAsync<Dictionary<string, SleeperPlayerDto>>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (payload is null || payload.Count == 0)
            {
                continue;
            }

            foreach (var (id, dto) in payload)
            {
                var mapped = MapPlayer(id, dto);
                if (mapped is null)
                {
                    continue;
                }

                byId[id] = mapped;
            }
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Sleeper live provider loaded {Count} players in {ElapsedMs} ms",
            byId.Count,
            stopwatch.ElapsedMilliseconds);

        if (byId.Count == 0)
        {
            throw new InvalidOperationException("Sleeper returned no mappable fantasy players.");
        }

        return byId.Values
            .OrderBy(p => p.Position)
            .ThenBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ToList();
    }

    private static Player? MapPlayer(string sleeperId, SleeperPlayerDto dto)
    {
        var position = MapPosition(dto.Position, dto.FantasyPositions);
        if (position is null)
        {
            return null;
        }

        var team = dto.Team ?? dto.TeamAbbr;
        if (position != Position.DST && string.IsNullOrWhiteSpace(team))
        {
            // Free agents clutter the explorer; keep rostered players + team defenses.
            return null;
        }

        var first = string.IsNullOrWhiteSpace(dto.FirstName) ? "Unknown" : dto.FirstName.Trim();
        var last = string.IsNullOrWhiteSpace(dto.LastName) ? sleeperId : dto.LastName.Trim();
        var full = string.IsNullOrWhiteSpace(dto.FullName) ? $"{first} {last}".Trim() : dto.FullName.Trim();

        return new Player
        {
            Id = SleeperPlayerIds.ToPlaybookId(sleeperId),
            FirstName = first,
            LastName = last,
            FullName = full,
            Position = position.Value,
            Team = string.IsNullOrWhiteSpace(team) ? "FA" : team.Trim(),
            JerseyNumber = dto.Number,
            Age = dto.Age,
            YearsPro = dto.YearsExp,
            College = dto.College,
            Height = FormatHeight(dto.Height),
            Weight = ParseWeight(dto.Weight),
            HeadshotUrl = null,
            Status = MapStatus(dto.Status, dto.InjuryStatus),
            ByeWeek = null
        };
    }

    private static Position? MapPosition(string? position, IReadOnlyList<string>? fantasyPositions)
    {
        var raw = position;
        if (string.IsNullOrWhiteSpace(raw) && fantasyPositions is { Count: > 0 })
        {
            raw = fantasyPositions[0];
        }

        return raw?.Trim().ToUpperInvariant() switch
        {
            "QB" => Position.QB,
            "RB" => Position.RB,
            "WR" => Position.WR,
            "TE" => Position.TE,
            "K" => Position.K,
            "DEF" or "DST" => Position.DST,
            _ => null
        };
    }

    private static PlayerStatus MapStatus(string? status, string? injuryStatus)
    {
        if (!string.IsNullOrWhiteSpace(injuryStatus))
        {
            return injuryStatus.Trim().ToUpperInvariant() switch
            {
                "Out" or "OUT" => PlayerStatus.Out,
                "Doubtful" or "DOUBTFUL" => PlayerStatus.Doubtful,
                "Questionable" or "QUESTIONABLE" => PlayerStatus.Questionable,
                "IR" or "Injured Reserve" or "INJURED RESERVE" => PlayerStatus.InjuredReserve,
                "PUP" or "NFI" or "SUS" or "Suspended" => PlayerStatus.Suspended,
                _ => PlayerStatus.Questionable
            };
        }

        return status?.Trim().ToUpperInvariant() switch
        {
            "ACTIVE" => PlayerStatus.Active,
            "INACTIVE" => PlayerStatus.PracticeSquad,
            "INJURED RESERVE" or "IR" => PlayerStatus.InjuredReserve,
            "SUSPENDED" => PlayerStatus.Suspended,
            "PRACTICE SQUAD" => PlayerStatus.PracticeSquad,
            _ => PlayerStatus.Active
        };
    }

    private static string? FormatHeight(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (int.TryParse(raw, out var inches) && inches is > 48 and < 96)
        {
            var feet = inches / 12;
            var rem = inches % 12;
            return $"{feet}'{rem}\"";
        }

        return raw;
    }

    private static int? ParseWeight(string? raw) =>
        int.TryParse(raw, out var weight) ? weight : null;

    private sealed class SleeperPlayerDto
    {
        [JsonPropertyName("player_id")]
        public string? PlayerId { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }

        [JsonPropertyName("position")]
        public string? Position { get; set; }

        [JsonPropertyName("fantasy_positions")]
        public List<string>? FantasyPositions { get; set; }

        [JsonPropertyName("team")]
        public string? Team { get; set; }

        [JsonPropertyName("team_abbr")]
        public string? TeamAbbr { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("injury_status")]
        public string? InjuryStatus { get; set; }

        [JsonPropertyName("number")]
        public int? Number { get; set; }

        [JsonPropertyName("age")]
        public int? Age { get; set; }

        [JsonPropertyName("years_exp")]
        public int? YearsExp { get; set; }

        [JsonPropertyName("college")]
        public string? College { get; set; }

        [JsonPropertyName("height")]
        public string? Height { get; set; }

        [JsonPropertyName("weight")]
        public string? Weight { get; set; }

        [JsonPropertyName("active")]
        public bool? Active { get; set; }
    }
}
