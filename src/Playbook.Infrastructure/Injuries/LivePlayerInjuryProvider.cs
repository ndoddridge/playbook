using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Players;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Players;
using Playbook.Infrastructure.Players;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Live CURRENT injuries from ESPN team injury reports, enriched with Sleeper practice/status fields.
/// Does NOT supply career historical injury records — ESPN feed is a current-report snapshot only.
/// </summary>
public sealed class LivePlayerInjuryProvider : IPlayerInjuryProvider
{
    public const string HttpClientName = "EspnInjuries";

    private static readonly Regex NonNameChars = new(@"[^a-z0-9\s]", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> TeamAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Arizona Cardinals"] = "ARI",
            ["Atlanta Falcons"] = "ATL",
            ["Baltimore Ravens"] = "BAL",
            ["Buffalo Bills"] = "BUF",
            ["Carolina Panthers"] = "CAR",
            ["Chicago Bears"] = "CHI",
            ["Cincinnati Bengals"] = "CIN",
            ["Cleveland Browns"] = "CLE",
            ["Dallas Cowboys"] = "DAL",
            ["Denver Broncos"] = "DEN",
            ["Detroit Lions"] = "DET",
            ["Green Bay Packers"] = "GB",
            ["Houston Texans"] = "HOU",
            ["Indianapolis Colts"] = "IND",
            ["Jacksonville Jaguars"] = "JAX",
            ["Kansas City Chiefs"] = "KC",
            ["Las Vegas Raiders"] = "LV",
            ["Los Angeles Chargers"] = "LAC",
            ["Los Angeles Rams"] = "LAR",
            ["Miami Dolphins"] = "MIA",
            ["Minnesota Vikings"] = "MIN",
            ["New England Patriots"] = "NE",
            ["New Orleans Saints"] = "NO",
            ["New York Giants"] = "NYG",
            ["New York Jets"] = "NYJ",
            ["Philadelphia Eagles"] = "PHI",
            ["Pittsburgh Steelers"] = "PIT",
            ["San Francisco 49ers"] = "SF",
            ["Seattle Seahawks"] = "SEA",
            ["Tampa Bay Buccaneers"] = "TB",
            ["Tennessee Titans"] = "TEN",
            ["Washington Commanders"] = "WAS"
        };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPlayerService _players;
    private readonly ILogger<LivePlayerInjuryProvider> _logger;

    public LivePlayerInjuryProvider(
        IHttpClientFactory httpClientFactory,
        IPlayerService players,
        ILogger<LivePlayerInjuryProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _players = players;
        _logger = logger;
    }

    public InjuryProviderKind Kind => InjuryProviderKind.Live;

    public string DisplayName => "Live (ESPN + Sleeper)";

    public InjuryProviderCapabilities Capabilities => InjuryProviderCapabilities.CurrentOnlyEspnSleeper;

    public async Task<IReadOnlyList<PlayerInjuryRecord>> GetInjuriesAsync(
        CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var payload = await client
            .GetFromJsonAsync<EspnInjuriesResponse>("site/v2/sports/football/nfl/injuries", cancellationToken)
            .ConfigureAwait(false);

        if (payload?.Injuries is null || payload.Injuries.Count == 0)
        {
            throw new InvalidOperationException("ESPN injuries feed returned no team injury groups.");
        }

        var players = _players.GetAllPlayers();
        var byNameTeam = players
            .GroupBy(p => LookupKey(p.FullName, p.Team))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var byName = players
            .GroupBy(p => NormalizeName(p.FullName))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var sleeperExtras = await LoadSleeperExtrasAsync(cancellationToken).ConfigureAwait(false);
        var season = payload.Season?.Year;
        var now = DateTimeOffset.UtcNow;
        var rows = new List<PlayerInjuryRecord>();

        foreach (var team in payload.Injuries)
        {
            var teamAbbr = ResolveTeamAbbr(team.DisplayName);
            foreach (var item in team.Injuries ?? [])
            {
                var athlete = item.Athlete;
                var name = athlete?.DisplayName ?? $"{athlete?.FirstName} {athlete?.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                Player? player = null;
                if (teamAbbr is not null &&
                    byNameTeam.TryGetValue(LookupKey(name, teamAbbr), out var matched))
                {
                    player = matched;
                }
                else if (byName.TryGetValue(NormalizeName(name), out var candidates))
                {
                    player = candidates.Count == 1
                        ? candidates[0]
                        : candidates.FirstOrDefault(c =>
                            teamAbbr is not null &&
                            string.Equals(c.Team, teamAbbr, StringComparison.OrdinalIgnoreCase));
                }

                if (player is null)
                {
                    continue;
                }

                sleeperExtras.TryGetValue(player.Id, out var extra);
                var status = NormalizeStatus(item.Status, item.Type?.Description, extra?.InjuryStatus);
                var date = ParseDate(item.Date) ?? now;
                var description = FirstNonEmpty(item.ShortComment, item.LongComment, extra?.InjuryNotes);
                var bodyPart = ExtractBodyPart(description, extra?.InjuryBodyPart);
                var practice = FirstNonEmpty(extra?.PracticeParticipation, extra?.PracticeDescription);
                var gameStatus = status;
                var sourceUrl = athlete?.Links?
                    .FirstOrDefault(l => l.Rel?.Contains("playercard") == true)?.Href;

                // Skip pure "Active" noise without injury context — ESPN lists many active notes.
                if (IsBenignActive(status, description, bodyPart, practice))
                {
                    continue;
                }

                rows.Add(new PlayerInjuryRecord
                {
                    PlayerId = player.Id,
                    Date = date,
                    Season = season,
                    Level = InjuryCompetitionLevel.Nfl,
                    Team = teamAbbr ?? player.Team,
                    Status = status,
                    BodyPart = bodyPart,
                    Description = description,
                    PracticeStatus = practice,
                    GameStatus = gameStatus,
                    Severity = InjurySeverityInference.FromStatus(status),
                    Source = "ESPN",
                    SourceUrl = sourceUrl,
                    Verified = true,
                    LastUpdated = now,
                    IsCurrent = true,
                    ExternalId = item.Id ?? $"{player.Id:N}:{date:O}:{status}"
                });
            }
        }

        // Add Sleeper-only designations for rostered players not present in ESPN feed.
        foreach (var (playerId, extra) in sleeperExtras)
        {
            if (string.IsNullOrWhiteSpace(extra.InjuryStatus) ||
                extra.InjuryStatus.Equals("NA", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rows.Any(r => r.PlayerId == playerId))
            {
                continue;
            }

            if (!players.Any(p => p.Id == playerId))
            {
                continue;
            }

            var status = NormalizeStatus(extra.InjuryStatus, null, extra.InjuryStatus);
            if (IsBenignActive(status, extra.InjuryNotes, extra.InjuryBodyPart, extra.PracticeParticipation))
            {
                continue;
            }

            var player = players.First(p => p.Id == playerId);
            rows.Add(new PlayerInjuryRecord
            {
                PlayerId = playerId,
                Date = ParseDate(extra.InjuryStartDate) ?? now,
                Season = season,
                Level = InjuryCompetitionLevel.Nfl,
                Team = player.Team,
                Status = status,
                BodyPart = NullIfEmpty(extra.InjuryBodyPart),
                Description = FirstNonEmpty(extra.InjuryNotes, $"{status} designation from Sleeper."),
                PracticeStatus = FirstNonEmpty(extra.PracticeParticipation, extra.PracticeDescription),
                GameStatus = status,
                Severity = InjurySeverityInference.FromStatus(status),
                Source = "Sleeper",
                SourceUrl = null,
                Verified = true,
                LastUpdated = now,
                IsCurrent = true,
                ExternalId = $"sleeper:{playerId:N}:{status}"
            });
        }

        watch.Stop();
        _logger.LogInformation(
            "ESPN/Sleeper injury provider loaded {Count} current records in {ElapsedMs} ms",
            rows.Count,
            watch.ElapsedMilliseconds);

        return rows;
    }

    private async Task<Dictionary<Guid, SleeperInjuryExtra>> LoadSleeperExtrasAsync(
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, SleeperInjuryExtra>();
        try
        {
            var client = _httpClientFactory.CreateClient(LivePlayerDataProvider.HttpClientName);
            foreach (var position in new[] { "QB", "RB", "WR", "TE" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = $"players/nfl?position={Uri.EscapeDataString(position)}&active=true";
                var payload = await client
                    .GetFromJsonAsync<Dictionary<string, SleeperPlayerInjuryDto>>(path, cancellationToken)
                    .ConfigureAwait(false);
                if (payload is null)
                {
                    continue;
                }

                foreach (var (id, dto) in payload)
                {
                    if (string.IsNullOrWhiteSpace(dto.InjuryStatus) &&
                        string.IsNullOrWhiteSpace(dto.PracticeParticipation))
                    {
                        continue;
                    }

                    map[SleeperPlayerIds.ToPlaybookId(id)] = new SleeperInjuryExtra(
                        dto.InjuryStatus,
                        dto.InjuryBodyPart,
                        dto.InjuryNotes,
                        dto.InjuryStartDate,
                        dto.PracticeParticipation,
                        dto.PracticeDescription);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Sleeper injury enrichment unavailable; continuing with ESPN only");
        }

        return map;
    }

    private static bool IsBenignActive(
        string status,
        string? description,
        string? bodyPart,
        string? practice)
    {
        if (!status.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
            !status.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Keep Active rows that clearly describe recovery / participation return.
        var blob = $"{description} {practice} {bodyPart}";
        return !ContainsAny(blob, "returned", "cleared", "full participant", "full practice", "no longer");
    }

    private static string NormalizeStatus(string? espnStatus, string? typeDescription, string? sleeperStatus)
    {
        var raw = FirstNonEmpty(espnStatus, typeDescription, sleeperStatus) ?? "Unknown";
        return raw.Trim() switch
        {
            "IR" or "Injured Reserve" => "Injured Reserve",
            "Sus" or "SUS" => "Suspended",
            "PUP" => "PUP",
            "COV" => "COVID",
            "DNR" => "Reserve/DNR",
            var s => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant())
        };
    }

    private static string? ExtractBodyPart(string? description, string? sleeperBodyPart)
    {
        if (!string.IsNullOrWhiteSpace(sleeperBodyPart) &&
            !sleeperBodyPart.Equals("Undisclosed", StringComparison.OrdinalIgnoreCase))
        {
            return sleeperBodyPart.Trim();
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return NullIfEmpty(sleeperBodyPart);
        }

        // Common pattern: "Name (knee) ..."
        var open = description.IndexOf('(');
        var close = description.IndexOf(')');
        if (open >= 0 && close > open + 1 && close - open <= 24)
        {
            return description[(open + 1)..close].Trim();
        }

        return NullIfEmpty(sleeperBodyPart);
    }

    private static string? ResolveTeamAbbr(string? displayName) =>
        displayName is not null && TeamAliases.TryGetValue(displayName, out var abbr) ? abbr : null;

    private static DateTimeOffset? ParseDate(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto
            : null;

    private static string LookupKey(string fullName, string? team) =>
        $"{NormalizeName(fullName)}|{team?.Trim().ToUpperInvariant()}";

    private static string NormalizeName(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        lower = NonNameChars.Replace(lower, " ");
        lower = Regex.Replace(lower, @"\s+", " ").Trim();
        return lower
            .Replace(" jr", "", StringComparison.Ordinal)
            .Replace(" sr", "", StringComparison.Ordinal)
            .Replace(" iii", "", StringComparison.Ordinal)
            .Replace(" ii", "", StringComparison.Ordinal)
            .Trim();
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ContainsAny(string text, params string[] phrases)
    {
        var hay = text.ToLowerInvariant();
        return phrases.Any(p => hay.Contains(p, StringComparison.Ordinal));
    }

    private sealed record SleeperInjuryExtra(
        string? InjuryStatus,
        string? InjuryBodyPart,
        string? InjuryNotes,
        string? InjuryStartDate,
        string? PracticeParticipation,
        string? PracticeDescription);

    private sealed class EspnInjuriesResponse
    {
        [JsonPropertyName("season")]
        public EspnSeasonDto? Season { get; set; }

        [JsonPropertyName("injuries")]
        public List<EspnTeamInjuriesDto>? Injuries { get; set; }
    }

    private sealed class EspnSeasonDto
    {
        [JsonPropertyName("year")]
        public int? Year { get; set; }
    }

    private sealed class EspnTeamInjuriesDto
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("injuries")]
        public List<EspnInjuryItemDto>? Injuries { get; set; }
    }

    private sealed class EspnInjuryItemDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("longComment")]
        public string? LongComment { get; set; }

        [JsonPropertyName("shortComment")]
        public string? ShortComment { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("athlete")]
        public EspnAthleteDto? Athlete { get; set; }

        [JsonPropertyName("type")]
        public EspnInjuryTypeDto? Type { get; set; }
    }

    private sealed class EspnInjuryTypeDto
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    private sealed class EspnAthleteDto
    {
        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("links")]
        public List<EspnLinkDto>? Links { get; set; }
    }

    private sealed class EspnLinkDto
    {
        [JsonPropertyName("rel")]
        public List<string>? Rel { get; set; }

        [JsonPropertyName("href")]
        public string? Href { get; set; }
    }

    private sealed class SleeperPlayerInjuryDto
    {
        [JsonPropertyName("injury_status")]
        public string? InjuryStatus { get; set; }

        [JsonPropertyName("injury_body_part")]
        public string? InjuryBodyPart { get; set; }

        [JsonPropertyName("injury_notes")]
        public string? InjuryNotes { get; set; }

        [JsonPropertyName("injury_start_date")]
        public string? InjuryStartDate { get; set; }

        [JsonPropertyName("practice_participation")]
        public string? PracticeParticipation { get; set; }

        [JsonPropertyName("practice_description")]
        public string? PracticeDescription { get; set; }
    }
}
