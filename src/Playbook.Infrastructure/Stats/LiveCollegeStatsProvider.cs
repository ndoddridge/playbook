using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Live college season statistics from ESPN college-football athlete endpoints.
/// Maps players via ESPN NFL roster ids (Sleeper rarely supplies espn_id for rookies).
/// Missing values stay null — never fabricated.
/// </summary>
public sealed class LiveCollegeStatsProvider : ICollegeStatsProvider
{
    public const string HttpClientName = "EspnCollegeStats";

    private static readonly Regex NonNameChars = new(@"[^a-z0-9\s]", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> NflTeamEspnIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ARI"] = "22", ["ATL"] = "1", ["BAL"] = "33", ["BUF"] = "2",
            ["CAR"] = "29", ["CHI"] = "3", ["CIN"] = "4", ["CLE"] = "5",
            ["DAL"] = "6", ["DEN"] = "7", ["DET"] = "8", ["GB"] = "9",
            ["HOU"] = "34", ["IND"] = "11", ["JAX"] = "30", ["JAC"] = "30",
            ["KC"] = "12", ["LV"] = "13", ["LAC"] = "24", ["LAR"] = "14",
            ["LA"] = "14", ["MIA"] = "15", ["MIN"] = "16", ["NE"] = "17",
            ["NO"] = "18", ["NYG"] = "19", ["NYJ"] = "20", ["PHI"] = "21",
            ["PIT"] = "23", ["SF"] = "25", ["SEA"] = "26", ["TB"] = "27",
            ["TEN"] = "10", ["WAS"] = "28", ["WSH"] = "28"
        };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CollegeStatsOptions _options;
    private readonly ILogger<LiveCollegeStatsProvider> _logger;

    public LiveCollegeStatsProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<CollegeStatsOptions> options,
        ILogger<LiveCollegeStatsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public CollegeStatsProviderKind Kind => CollegeStatsProviderKind.Live;

    public string DisplayName => "Live (ESPN College)";

    public async Task<IReadOnlyList<PlayerSeasonStats>> GetCollegeStatsAsync(
        CollegeStatsSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var young = request.Candidates
            .Where(c => (c.YearsPro ?? 0) < 3)
            .Where(c => !string.IsNullOrWhiteSpace(c.FullName))
            .GroupBy(c => c.PlayerId)
            .Select(g => g.First())
            .Take(Math.Clamp(_options.MaxAthletesPerSync, 25, 800))
            .ToList();

        if (young.Count == 0)
        {
            return [];
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var idMap = await BuildEspnIdMapAsync(client, young, cancellationToken).ConfigureAwait(false);

        var resolved = new List<(CollegePlayerCandidate Candidate, string EspnId)>();
        foreach (var candidate in young)
        {
            var espnId = candidate.EspnAthleteId;
            if (string.IsNullOrWhiteSpace(espnId))
            {
                idMap.TryGetValue(LookupKey(candidate.FullName, candidate.Team), out espnId);
            }

            if (string.IsNullOrWhiteSpace(espnId))
            {
                idMap.TryGetValue(NormalizeName(candidate.FullName), out espnId);
            }

            if (!string.IsNullOrWhiteSpace(espnId))
            {
                resolved.Add((candidate, espnId!));
            }
        }

        var bag = new ConcurrentBag<PlayerSeasonStats>();
        var parallelism = Math.Clamp(_options.MaxConcurrency, 2, 16);
        await Parallel.ForEachAsync(
            resolved,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = cancellationToken
            },
            async (item, ct) =>
            {
                try
                {
                    var seasons = await FetchCollegeSeasonsAsync(client, item.Candidate, item.EspnId, ct)
                        .ConfigureAwait(false);
                    foreach (var row in seasons)
                    {
                        bag.Add(row);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(
                        ex,
                        "College stats unavailable for {Player} (ESPN {EspnId})",
                        item.Candidate.FullName,
                        item.EspnId);
                }
            }).ConfigureAwait(false);

        stopwatch.Stop();
        var rows = bag.ToList();
        _logger.LogInformation(
            "ESPN college provider loaded {Seasons} seasons for {Players} players in {ElapsedMs} ms " +
            "({Resolved}/{Young} ids resolved)",
            rows.Count,
            rows.Select(r => r.PlayerId).Distinct().Count(),
            stopwatch.ElapsedMilliseconds,
            resolved.Count,
            young.Count);

        return rows;
    }

    private async Task<Dictionary<string, string>> BuildEspnIdMapAsync(
        HttpClient client,
        IReadOnlyList<CollegePlayerCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var teams = candidates
            .Select(c => c.Team)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim().ToUpperInvariant())
            .Where(t => NflTeamEspnIds.ContainsKey(t))
            .Distinct()
            .ToList();

        // Always include a few high-volume teams so free agents matched by name alone still work
        // when their team abbreviation is present on another roster fetch.
        foreach (var team in teams)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!NflTeamEspnIds.TryGetValue(team, out var espnTeamId))
            {
                continue;
            }

            try
            {
                var roster = await client
                    .GetFromJsonAsync<EspnRosterResponse>(
                        $"site/v2/sports/football/nfl/teams/{espnTeamId}/roster",
                        cancellationToken)
                    .ConfigureAwait(false);

                foreach (var athlete in EnumerateRosterAthletes(roster))
                {
                    if (string.IsNullOrWhiteSpace(athlete.Id) ||
                        string.IsNullOrWhiteSpace(athlete.FullName ?? athlete.DisplayName))
                    {
                        continue;
                    }

                    var name = athlete.FullName ?? athlete.DisplayName!;
                    map[NormalizeName(name)] = athlete.Id;
                    map[LookupKey(name, team)] = athlete.Id;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Unable to load ESPN roster for team {Team}", team);
            }
        }

        return map;
    }

    private static IEnumerable<EspnAthleteDto> EnumerateRosterAthletes(EspnRosterResponse? roster)
    {
        if (roster?.Athletes is null)
        {
            yield break;
        }

        foreach (var group in roster.Athletes)
        {
            if (group.Items is null)
            {
                continue;
            }

            foreach (var athlete in group.Items)
            {
                yield return athlete;
            }
        }
    }

    private async Task<IReadOnlyList<PlayerSeasonStats>> FetchCollegeSeasonsAsync(
        HttpClient client,
        CollegePlayerCandidate candidate,
        string espnId,
        CancellationToken cancellationToken)
    {
        using var response = await client
            .GetAsync(
                $"common/v3/sports/football/college-football/athletes/{Uri.EscapeDataString(espnId)}/stats",
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var payload = await JsonSerializer
            .DeserializeAsync<EspnCollegeStatsResponse>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (payload?.Categories is null || payload.Categories.Count == 0)
        {
            return [];
        }

        var schoolByTeamId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (payload.Teams is not null)
        {
            foreach (var (key, team) in payload.Teams)
            {
                var school = team.ShortDisplayName ?? team.Location ?? team.DisplayName ?? key;
                if (!string.IsNullOrWhiteSpace(team.Id))
                {
                    schoolByTeamId[team.Id] = school!;
                }

                if (!string.IsNullOrWhiteSpace(team.Slug))
                {
                    schoolByTeamId[team.Slug] = school!;
                }
            }
        }

        var bySeason = new Dictionary<int, SeasonAccumulator>();

        foreach (var category in payload.Categories)
        {
            if (category.Statistics is null || category.Names is null)
            {
                continue;
            }

            var index = IndexNames(category.Names);
            foreach (var row in category.Statistics)
            {
                var year = row.Season?.Year;
                if (year is null or < 1990 or > 2100)
                {
                    continue;
                }

                if (!bySeason.TryGetValue(year.Value, out var acc))
                {
                    var school = ResolveSchool(row, schoolByTeamId, candidate.College);
                    acc = new SeasonAccumulator(year.Value, school);
                    bySeason[year.Value] = acc;
                }

                ApplyCategory(acc, category.Name, index, row.Stats);
            }
        }

        var now = DateTimeOffset.UtcNow;
        return bySeason.Values
            .Select(acc => acc.ToStats(candidate.PlayerId, now))
            .Where(r => r.HasAnyCountingStat)
            .OrderByDescending(r => r.Season)
            .ToList();
    }

    private static string ResolveSchool(
        EspnSeasonStatRow row,
        IReadOnlyDictionary<string, string> schoolByTeamId,
        string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(row.TeamId) &&
            schoolByTeamId.TryGetValue(row.TeamId, out var byId))
        {
            return byId;
        }

        if (!string.IsNullOrWhiteSpace(row.TeamSlug) &&
            schoolByTeamId.TryGetValue(row.TeamSlug, out var bySlug))
        {
            return bySlug;
        }

        if (!string.IsNullOrWhiteSpace(row.TeamSlug))
        {
            return HumanizeSlug(row.TeamSlug);
        }

        return fallback ?? "College";
    }

    private static string HumanizeSlug(string slug)
    {
        var trimmed = slug.Replace("-trojans", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-rebels", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-tigers", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-bulldogs", "", StringComparison.OrdinalIgnoreCase)
            .Replace('-', ' ')
            .Trim();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed);
    }

    private static Dictionary<string, int> IndexNames(IReadOnlyList<string> names)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Count; i++)
        {
            map[names[i]] = i;
        }

        return map;
    }

    private static void ApplyCategory(
        SeasonAccumulator acc,
        string? categoryName,
        IReadOnlyDictionary<string, int> index,
        IReadOnlyList<string>? stats)
    {
        if (stats is null)
        {
            return;
        }

        switch (categoryName?.Trim().ToLowerInvariant())
        {
            case "passing":
                acc.PassCompletions = ReadInt(stats, index, "completions") ?? acc.PassCompletions;
                acc.PassAttempts = ReadInt(stats, index, "passingAttempts") ?? acc.PassAttempts;
                acc.PassYards = ReadInt(stats, index, "passingYards") ?? acc.PassYards;
                acc.PassTouchdowns = ReadInt(stats, index, "passingTouchdowns") ?? acc.PassTouchdowns;
                acc.PassInterceptions = ReadInt(stats, index, "interceptions") ?? acc.PassInterceptions;
                break;
            case "rushing":
                acc.RushAttempts = ReadInt(stats, index, "rushingAttempts") ?? acc.RushAttempts;
                acc.RushYards = ReadInt(stats, index, "rushingYards") ?? acc.RushYards;
                acc.RushTouchdowns = ReadInt(stats, index, "rushingTouchdowns") ?? acc.RushTouchdowns;
                break;
            case "receiving":
                acc.Receptions = ReadInt(stats, index, "receptions") ?? acc.Receptions;
                acc.ReceivingYards = ReadInt(stats, index, "receivingYards") ?? acc.ReceivingYards;
                acc.ReceivingTouchdowns = ReadInt(stats, index, "receivingTouchdowns") ?? acc.ReceivingTouchdowns;
                // ESPN college receiving tables omit targets; leave null rather than inventing.
                break;
        }
    }

    private static int? ReadInt(
        IReadOnlyList<string> stats,
        IReadOnlyDictionary<string, int> index,
        string name)
    {
        if (!index.TryGetValue(name, out var i) || i < 0 || i >= stats.Count)
        {
            return null;
        }

        var raw = stats[i]?.Replace(",", "", StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(raw) || raw is "-" or "--")
        {
            return null;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string LookupKey(string fullName, string? team) =>
        $"{NormalizeName(fullName)}|{team?.Trim().ToUpperInvariant()}";

    private static string NormalizeName(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        lower = NonNameChars.Replace(lower, " ");
        lower = Regex.Replace(lower, @"\s+", " ").Trim();
        lower = lower
            .Replace(" jr", "", StringComparison.Ordinal)
            .Replace(" sr", "", StringComparison.Ordinal)
            .Replace(" iii", "", StringComparison.Ordinal)
            .Replace(" ii", "", StringComparison.Ordinal)
            .Trim();
        return lower;
    }

    private sealed class SeasonAccumulator(int season, string school)
    {
        public int Season { get; } = season;
        public string School { get; } = school;
        public int? PassAttempts { get; set; }
        public int? PassCompletions { get; set; }
        public int? PassYards { get; set; }
        public int? PassTouchdowns { get; set; }
        public int? PassInterceptions { get; set; }
        public int? RushAttempts { get; set; }
        public int? RushYards { get; set; }
        public int? RushTouchdowns { get; set; }
        public int? Receptions { get; set; }
        public int? ReceivingYards { get; set; }
        public int? ReceivingTouchdowns { get; set; }

        public PlayerSeasonStats ToStats(Guid playerId, DateTimeOffset now)
        {
            var passYds = PassYards ?? 0;
            var passTd = PassTouchdowns ?? 0;
            var interceptions = PassInterceptions ?? 0;
            var rushYds = RushYards ?? 0;
            var rushTd = RushTouchdowns ?? 0;
            var rec = Receptions ?? 0;
            var recYds = ReceivingYards ?? 0;
            var recTd = ReceivingTouchdowns ?? 0;

            var standard = passYds / 25m + passTd * 4m - interceptions * 2m
                           + rushYds / 10m + rushTd * 6m
                           + recYds / 10m + recTd * 6m;
            var half = standard + rec * 0.5m;
            var ppr = standard + rec;

            return new PlayerSeasonStats
            {
                PlayerId = playerId,
                Season = Season,
                SeasonType = "college",
                Period = StatsPeriod.College,
                Games = null,
                Starts = null,
                PassAttempts = PassAttempts,
                PassCompletions = PassCompletions,
                PassYards = PassYards,
                PassTouchdowns = PassTouchdowns,
                PassInterceptions = PassInterceptions,
                RushAttempts = RushAttempts,
                RushYards = RushYards,
                RushTouchdowns = RushTouchdowns,
                Targets = null,
                Receptions = Receptions,
                ReceivingYards = ReceivingYards,
                ReceivingTouchdowns = ReceivingTouchdowns,
                FantasyPointsStandard = Round(standard),
                FantasyPointsHalfPpr = Round(half),
                FantasyPointsPpr = Round(ppr),
                CollegeSchool = School,
                SourceProvider = "ESPN",
                LastUpdated = now
            };
        }

        private static decimal Round(decimal value) =>
            Math.Round(value, 1, MidpointRounding.AwayFromZero);
    }

    private sealed class EspnRosterResponse
    {
        [JsonPropertyName("athletes")]
        public List<EspnRosterGroup>? Athletes { get; set; }
    }

    private sealed class EspnRosterGroup
    {
        [JsonPropertyName("items")]
        public List<EspnAthleteDto>? Items { get; set; }
    }

    private sealed class EspnAthleteDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }

    private sealed class EspnCollegeStatsResponse
    {
        [JsonPropertyName("teams")]
        public Dictionary<string, EspnTeamDto>? Teams { get; set; }

        [JsonPropertyName("categories")]
        public List<EspnCategoryDto>? Categories { get; set; }
    }

    private sealed class EspnTeamDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("shortDisplayName")]
        public string? ShortDisplayName { get; set; }
    }

    private sealed class EspnCategoryDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("names")]
        public List<string>? Names { get; set; }

        [JsonPropertyName("statistics")]
        public List<EspnSeasonStatRow>? Statistics { get; set; }
    }

    private sealed class EspnSeasonStatRow
    {
        [JsonPropertyName("teamId")]
        public string? TeamId { get; set; }

        [JsonPropertyName("teamSlug")]
        public string? TeamSlug { get; set; }

        [JsonPropertyName("season")]
        public EspnSeasonDto? Season { get; set; }

        [JsonPropertyName("stats")]
        public List<string>? Stats { get; set; }
    }

    private sealed class EspnSeasonDto
    {
        [JsonPropertyName("year")]
        public int? Year { get; set; }
    }
}
