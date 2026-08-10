using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Players;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Historical NFL injury reports from nflverse public CSVs (official weekly injury reports, 2009+).
/// Free, machine-readable, no API key. Maps to Playbook players via GSIS IDs from the identity directory.
/// Does not fabricate college injuries or games-missed when absent.
/// </summary>
public sealed class NflverseHistoricalInjuryProvider : IHistoricalInjuryProvider
{
    public const string HttpClientName = "NflverseInjuries";

    private const string ReleaseBase =
        "https://github.com/nflverse/nflverse-data/releases/download/injuries/injuries_{0}.csv";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPlayerIdentityDirectory _identities;
    private readonly InjuryOptions _options;
    private readonly ILogger<NflverseHistoricalInjuryProvider> _logger;

    public int LastMatchedRows { get; private set; }

    public int LastUnresolvedRows { get; private set; }

    public TimeSpan LastResponseTime { get; private set; }

    public string? LastError { get; private set; }

    public NflverseHistoricalInjuryProvider(
        IHttpClientFactory httpClientFactory,
        IPlayerIdentityDirectory identities,
        IOptions<InjuryOptions> options,
        ILogger<NflverseHistoricalInjuryProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _identities = identities;
        _options = options.Value;
        _logger = logger;
    }

    public HistoricalInjuryProviderKind Kind => HistoricalInjuryProviderKind.Nflverse;

    public string DisplayName => "nflverse (historical NFL)";

    public bool IsConfigured => true;

    public async Task<IReadOnlyList<PlayerInjuryRecord>> GetHistoricalInjuriesAsync(
        CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        LastError = null;
        LastMatchedRows = 0;
        LastUnresolvedRows = 0;

        if (_identities.IdentitiesWithGsisId == 0)
        {
            // Players must load first so GSIS crosswalk exists.
            LastError = "Player identity directory has no GSIS ids yet — load players before historical injuries.";
            _logger.LogWarning("{Message}", LastError);
            watch.Stop();
            LastResponseTime = watch.Elapsed;
            return [];
        }

        var seasonCount = Math.Clamp(_options.HistoricalSeasonCount, 1, 17);
        var currentSeason = DateTime.UtcNow.Month >= 3 ? DateTime.UtcNow.Year : DateTime.UtcNow.Year - 1;
        var seasons = Enumerable.Range(currentSeason - seasonCount + 1, seasonCount).ToList();
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var rows = new List<PlayerInjuryRecord>();
        var unresolved = 0;
        var matched = 0;

        foreach (var season in seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url = string.Format(CultureInfo.InvariantCulture, ReleaseBase, season);
                using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("nflverse injuries_{Season}.csv not found — skipping", season);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                var columns = SplitCsv(header);
                var index = BuildIndex(columns);

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var fields = SplitCsv(line);
                    var record = MapRow(fields, index, season, ref matched, ref unresolved);
                    if (record is not null)
                    {
                        rows.Add(record);
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = $"Season {season}: {ex.Message}";
                _logger.LogWarning(ex, "Failed loading nflverse injuries for {Season}", season);
            }
        }

        watch.Stop();
        LastResponseTime = watch.Elapsed;
        LastMatchedRows = matched;
        LastUnresolvedRows = unresolved;

        _logger.LogInformation(
            "nflverse historical injuries: {Rows} mapped rows across {Seasons} seasons ({Matched} matched, {Unresolved} unresolved GSIS) in {Ms} ms",
            rows.Count,
            seasons.Count,
            matched,
            unresolved,
            watch.ElapsedMilliseconds);

        return rows;
    }

    private PlayerInjuryRecord? MapRow(
        string[] fields,
        Dictionary<string, int> index,
        int seasonFallback,
        ref int matched,
        ref int unresolved)
    {
        var gsis = Get(fields, index, "gsis_id");
        if (string.IsNullOrWhiteSpace(gsis))
        {
            return null;
        }

        var reportStatus = Get(fields, index, "report_status");
        var primary = FirstNonEmpty(
            Get(fields, index, "report_primary_injury"),
            Get(fields, index, "practice_primary_injury"));
        var practiceStatus = Get(fields, index, "practice_status");
        var practicePrimary = Get(fields, index, "practice_primary_injury");

        // Skip pure rest / empty rows — not injury history.
        var isRestOnly =
            (!string.IsNullOrWhiteSpace(practicePrimary) &&
             practicePrimary.Contains("not injury related", StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(primary) &&
             primary.Contains("not injury related", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(reportStatus) && string.IsNullOrWhiteSpace(primary))
        {
            return null;
        }

        if (isRestOnly && string.IsNullOrWhiteSpace(reportStatus))
        {
            return null;
        }

        if (isRestOnly &&
            string.IsNullOrWhiteSpace(Get(fields, index, "report_primary_injury")))
        {
            return null;
        }

        var identity = _identities.GetByGsisId(gsis);
        if (identity is null)
        {
            unresolved++;
            return null;
        }

        matched++;
        var week = ParseInt(Get(fields, index, "week"));
        var season = ParseInt(Get(fields, index, "season")) ?? seasonFallback;
        var team = Get(fields, index, "team");
        var secondary = FirstNonEmpty(
            Get(fields, index, "report_secondary_injury"),
            Get(fields, index, "practice_secondary_injury"));
        var status = string.IsNullOrWhiteSpace(reportStatus) ? "Listed" : reportStatus.Trim();
        var bodyPart = string.IsNullOrWhiteSpace(primary) ? null : primary.Trim();
        var description = BuildDescription(status, primary, secondary, practiceStatus);
        var modified = ParseDate(Get(fields, index, "date_modified"))
                       ?? ApproximateWeekDate(season, week);
        var severity = InjurySeverityInference.FromStatus(status);

        return new PlayerInjuryRecord
        {
            PlayerId = identity.PlaybookId,
            Date = modified,
            Season = season,
            Week = week,
            Level = InjuryCompetitionLevel.Nfl,
            Team = string.IsNullOrWhiteSpace(team) ? identity.Team : team,
            BodyPart = bodyPart,
            InjuryType = secondary,
            Description = description,
            Status = status,
            PracticeStatus = string.IsNullOrWhiteSpace(practiceStatus) ? null : practiceStatus.Trim(),
            GameStatus = string.IsNullOrWhiteSpace(reportStatus) ? null : reportStatus.Trim(),
            GamesMissed = null,
            Severity = severity,
            Source = "nflverse",
            SourceUrl =
                $"https://github.com/nflverse/nflverse-data/releases/tag/injuries",
            Verified = true,
            SourceConfidence = InjurySourceConfidence.Verified,
            LastUpdated = modified,
            IsCurrent = false,
            GsisId = gsis,
            ExternalId = $"nflverse:{gsis}:{season}:{week}:{status}:{bodyPart}"
        };
    }

    private static string BuildDescription(
        string status,
        string? primary,
        string? secondary,
        string? practiceStatus)
    {
        var parts = new List<string> { status };
        if (!string.IsNullOrWhiteSpace(primary))
        {
            parts.Add(primary);
        }

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            parts.Add($"also {secondary}");
        }

        if (!string.IsNullOrWhiteSpace(practiceStatus))
        {
            parts.Add(practiceStatus);
        }

        return string.Join(" — ", parts);
    }

    private static DateTimeOffset ApproximateWeekDate(int season, int? week)
    {
        // NFL week 1 roughly first week of September.
        var start = new DateTimeOffset(season, 9, 5, 0, 0, 0, TimeSpan.Zero);
        if (week is null or < 1)
        {
            return start;
        }

        return start.AddDays((week.Value - 1) * 7);
    }

    private static Dictionary<string, int> BuildIndex(string[] columns)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Length; i++)
        {
            index[columns[i].Trim()] = i;
        }

        return index;
    }

    private static string? Get(string[] fields, Dictionary<string, int> index, string column)
    {
        if (!index.TryGetValue(column, out var i) || i < 0 || i >= fields.Length)
        {
            return null;
        }

        var value = fields[i].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string[] SplitCsv(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        result.Add(current.ToString());
        return result.ToArray();
    }

    private static int? ParseInt(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTimeOffset? ParseDate(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
