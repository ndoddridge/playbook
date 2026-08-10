using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Players;
using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Historical NFL weekly player statistics from nflverse public CSVs (game-by-game, free, no API key).
/// Season files are cached on disk for incremental updates; only requested seasons are downloaded.
/// Maps to Playbook players via GSIS IDs from the identity directory.
/// </summary>
public sealed class NflversePlayerStatsProvider : IHistoricalPlayerStatsProvider
{
    public const string HttpClientName = "NflversePlayerStats";

    private const string ReleaseUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/player_stats/player_stats_{0}.csv.gz";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPlayerIdentityDirectory _identities;
    private readonly PlayerStatsOptions _options;
    private readonly ILogger<NflversePlayerStatsProvider> _logger;
    private readonly string _cacheDir;

    public int LastMatchedPlayers { get; private set; }

    public int LastUnresolvedPlayers { get; private set; }

    public TimeSpan LastResponseTime { get; private set; }

    public string? LastError { get; private set; }

    public NflversePlayerStatsProvider(
        IHttpClientFactory httpClientFactory,
        IPlayerIdentityDirectory identities,
        IOptions<PlayerStatsOptions> options,
        ILogger<NflversePlayerStatsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _identities = identities;
        _options = options.Value;
        _logger = logger;
        _cacheDir = Path.Combine(AppContext.BaseDirectory, "data", "nflverse-player-stats");
        Directory.CreateDirectory(_cacheDir);
    }

    public HistoricalPlayerStatsProviderKind Kind => HistoricalPlayerStatsProviderKind.Nflverse;

    public string DisplayName => "nflverse (player stats)";

    public bool IsConfigured => true;

    public async Task<HistoricalPlayerStatsBatch> GetHistoricalStatsAsync(
        HistoricalPlayerStatsSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        LastError = null;
        LastMatchedPlayers = 0;
        LastUnresolvedPlayers = 0;

        if (_identities.IdentitiesWithGsisId == 0)
        {
            LastError = "Player identity directory has no GSIS ids yet — load players before historical stats.";
            _logger.LogWarning("{Message}", LastError);
            watch.Stop();
            LastResponseTime = watch.Elapsed;
            return new HistoricalPlayerStatsBatch
            {
                Error = LastError,
                ResponseTime = watch.Elapsed
            };
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var gameLogs = new List<PlayerGameStats>();
        var seasonBuckets = new Dictionary<(Guid PlayerId, int Season), SeasonAccumulator>();
        var matchedPlayers = new HashSet<Guid>();
        var unresolvedGsis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        string? error = null;

        foreach (var season in request.Seasons.Distinct().OrderByDescending(s => s))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var path = await EnsureSeasonFileAsync(client, season, request.ForceRedownload, cancellationToken)
                    .ConfigureAwait(false);
                if (path is null)
                {
                    _logger.LogInformation("nflverse player_stats_{Season} unavailable — skipping", season);
                    continue;
                }

                await using var file = File.OpenRead(path);
                await using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8);
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
                    var mapped = MapGameRow(
                        fields,
                        index,
                        season,
                        now,
                        matchedPlayers,
                        unresolvedGsis);
                    if (mapped is null)
                    {
                        continue;
                    }

                    gameLogs.Add(mapped.Value.Game);
                    Accumulate(seasonBuckets, mapped.Value.Game, mapped.Value.Position);
                }
            }
            catch (Exception ex)
            {
                error = string.IsNullOrWhiteSpace(error) ? ex.Message : $"{error}; {ex.Message}";
                _logger.LogWarning(ex, "Failed loading nflverse player stats for {Season}", season);
            }
        }

        var seasonRecords = seasonBuckets.Values
            .Select(a => a.ToSeasonStats(now))
            .Where(r => r.HasAnyCountingStat || r.Games is > 0)
            .ToList();

        watch.Stop();
        LastResponseTime = watch.Elapsed;
        LastMatchedPlayers = matchedPlayers.Count;
        LastUnresolvedPlayers = unresolvedGsis.Count;
        LastError = error;

        _logger.LogInformation(
            "nflverse stats: {Seasons} season rows, {Games} game logs, {Matched} players matched, {Unresolved} unresolved GSIS in {Ms} ms",
            seasonRecords.Count,
            gameLogs.Count,
            matchedPlayers.Count,
            unresolvedGsis.Count,
            watch.ElapsedMilliseconds);

        return new HistoricalPlayerStatsBatch
        {
            SeasonRecords = seasonRecords,
            GameLogs = gameLogs,
            IdentityMatches = matchedPlayers.Count,
            UnresolvedPlayers = unresolvedGsis.Count,
            Error = error,
            ResponseTime = watch.Elapsed
        };
    }

    private async Task<string?> EnsureSeasonFileAsync(
        HttpClient client,
        int season,
        bool forceRedownload,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_cacheDir, $"player_stats_{season}.csv.gz");
        if (!forceRedownload && File.Exists(path) && new FileInfo(path).Length > 100)
        {
            return path;
        }

        var url = string.Format(CultureInfo.InvariantCulture, ReleaseUrl, season);
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var temp = path + ".tmp";
        await using (var local = File.Create(temp))
        {
            await remote.CopyToAsync(local, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
        _logger.LogInformation("Cached nflverse player_stats_{Season} at {Path}", season, path);
        return path;
    }

    private (PlayerGameStats Game, string? Position)? MapGameRow(
        string[] fields,
        Dictionary<string, int> index,
        int seasonFallback,
        DateTimeOffset now,
        HashSet<Guid> matchedPlayers,
        HashSet<string> unresolvedGsis)
    {
        var gsis = Get(fields, index, "player_id");
        if (string.IsNullOrWhiteSpace(gsis))
        {
            return null;
        }

        var seasonType = Get(fields, index, "season_type") ?? "REG";
        if (!string.Equals(seasonType, "REG", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(seasonType, "regular", StringComparison.OrdinalIgnoreCase))
        {
            // Keep regular-season sample clean for trends; playoff rows can be added later.
            return null;
        }

        var identity = _identities.GetByGsisId(gsis);
        if (identity is null)
        {
            // Sleeper often omits GSIS for younger players — fall back to name + team.
            var displayName = Get(fields, index, "player_display_name")
                              ?? Get(fields, index, "player_name");
            var team = Get(fields, index, "recent_team");
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                identity = _identities.ResolveByNameTeam(displayName, team);
            }
        }

        if (identity is null)
        {
            unresolvedGsis.Add(gsis);
            return null;
        }

        matchedPlayers.Add(identity.PlaybookId);

        var week = ParseInt(Get(fields, index, "week")) ?? 0;
        var season = ParseInt(Get(fields, index, "season")) ?? seasonFallback;
        if (week <= 0)
        {
            return null;
        }

        var position = Get(fields, index, "position") ?? identity.Position;
        var passAtt = ParseInt(Get(fields, index, "attempts"));
        var passCmp = ParseInt(Get(fields, index, "completions"));
        var passYds = ParseInt(Get(fields, index, "passing_yards"));
        var passTd = ParseInt(Get(fields, index, "passing_tds"));
        var ints = ParseInt(Get(fields, index, "interceptions"));
        var rushAtt = ParseInt(Get(fields, index, "carries"));
        var rushYds = ParseInt(Get(fields, index, "rushing_yards"));
        var rushTd = ParseInt(Get(fields, index, "rushing_tds"));
        var targets = ParseInt(Get(fields, index, "targets"));
        var receptions = ParseInt(Get(fields, index, "receptions"));
        var recYds = ParseInt(Get(fields, index, "receiving_yards"));
        var recTd = ParseInt(Get(fields, index, "receiving_tds"));

        var rushFumbles = ParseInt(Get(fields, index, "rushing_fumbles"));
        var recFumbles = ParseInt(Get(fields, index, "receiving_fumbles"));
        var sackFumbles = ParseInt(Get(fields, index, "sack_fumbles"));
        int? fumbles = null;
        if (rushFumbles is not null || recFumbles is not null || sackFumbles is not null)
        {
            fumbles = (rushFumbles ?? 0) + (recFumbles ?? 0) + (sackFumbles ?? 0);
        }

        var counting = new CanonicalCountingStats
        {
            PassAttempts = passAtt,
            PassCompletions = passCmp,
            PassYards = passYds,
            PassTouchdowns = passTd,
            PassInterceptions = ints,
            RushAttempts = rushAtt,
            RushYards = rushYds,
            RushTouchdowns = rushTd,
            Targets = targets,
            Receptions = receptions,
            ReceivingYards = recYds,
            ReceivingTouchdowns = recTd,
            Fumbles = fumbles
        };
        var (_, missing) = StatsQuality.Evaluate(counting, position);

        var game = new PlayerGameStats
        {
            PlayerId = identity.PlaybookId,
            Season = season,
            Week = week,
            SeasonType = "regular",
            Level = FootballLevel.Nfl,
            OpponentTeam = Get(fields, index, "opponent_team"),
            Team = Get(fields, index, "recent_team"),
            Position = position,
            PassAttempts = passAtt,
            PassCompletions = passCmp,
            PassYards = passYds,
            PassTouchdowns = passTd,
            PassInterceptions = ints,
            RushAttempts = rushAtt,
            RushYards = rushYds,
            RushTouchdowns = rushTd,
            Targets = targets,
            Receptions = receptions,
            ReceivingYards = recYds,
            ReceivingTouchdowns = recTd,
            Fumbles = fumbles,
            SourceProvider = "nflverse",
            Source = $"player_stats_{season}.csv.gz",
            IdentityMatch = StatsIdentityMatch.Matched,
            MissingFields = missing,
            LastUpdated = now
        };

        return (game, position);
    }

    private static void Accumulate(
        Dictionary<(Guid PlayerId, int Season), SeasonAccumulator> buckets,
        PlayerGameStats game,
        string? position)
    {
        var key = (game.PlayerId, game.Season);
        if (!buckets.TryGetValue(key, out var acc))
        {
            acc = new SeasonAccumulator(game.PlayerId, game.Season, position);
            buckets[key] = acc;
        }

        acc.Add(game);
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

    private static string? Get(string[] fields, Dictionary<string, int> index, string name) =>
        index.TryGetValue(name, out var i) && i < fields.Length ? NullIfEmpty(fields[i]) : null;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            return i;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return (int)Math.Round(d, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    private static string[] SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
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
                result.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        result.Add(sb.ToString());
        return result.ToArray();
    }

    private sealed class SeasonAccumulator
    {
        private readonly Guid _playerId;
        private readonly int _season;
        private readonly string? _position;
        private int _games;
        private int? _passAtt, _passCmp, _passYds, _passTd, _ints;
        private int? _rushAtt, _rushYds, _rushTd;
        private int? _targets, _rec, _recYds, _recTd;
        private int? _fumbles;
        private bool _sawPassAtt, _sawPassCmp, _sawPassYds, _sawPassTd, _sawInts;
        private bool _sawRushAtt, _sawRushYds, _sawRushTd;
        private bool _sawTargets, _sawRec, _sawRecYds, _sawRecTd, _sawFumbles;

        public SeasonAccumulator(Guid playerId, int season, string? position)
        {
            _playerId = playerId;
            _season = season;
            _position = position;
        }

        public void Add(PlayerGameStats game)
        {
            _games++;
            Add(ref _passAtt, ref _sawPassAtt, game.PassAttempts);
            Add(ref _passCmp, ref _sawPassCmp, game.PassCompletions);
            Add(ref _passYds, ref _sawPassYds, game.PassYards);
            Add(ref _passTd, ref _sawPassTd, game.PassTouchdowns);
            Add(ref _ints, ref _sawInts, game.PassInterceptions);
            Add(ref _rushAtt, ref _sawRushAtt, game.RushAttempts);
            Add(ref _rushYds, ref _sawRushYds, game.RushYards);
            Add(ref _rushTd, ref _sawRushTd, game.RushTouchdowns);
            Add(ref _targets, ref _sawTargets, game.Targets);
            Add(ref _rec, ref _sawRec, game.Receptions);
            Add(ref _recYds, ref _sawRecYds, game.ReceivingYards);
            Add(ref _recTd, ref _sawRecTd, game.ReceivingTouchdowns);
            Add(ref _fumbles, ref _sawFumbles, game.Fumbles);
        }

        private static void Add(ref int? total, ref bool saw, int? value)
        {
            if (value is null)
            {
                return;
            }

            saw = true;
            total = (total ?? 0) + value.Value;
        }

        public PlayerSeasonStats ToSeasonStats(DateTimeOffset now)
        {
            var counting = new CanonicalCountingStats
            {
                PassAttempts = _sawPassAtt ? _passAtt : null,
                PassCompletions = _sawPassCmp ? _passCmp : null,
                PassYards = _sawPassYds ? _passYds : null,
                PassTouchdowns = _sawPassTd ? _passTd : null,
                PassInterceptions = _sawInts ? _ints : null,
                RushAttempts = _sawRushAtt ? _rushAtt : null,
                RushYards = _sawRushYds ? _rushYds : null,
                RushTouchdowns = _sawRushTd ? _rushTd : null,
                Targets = _sawTargets ? _targets : null,
                Receptions = _sawRec ? _rec : null,
                ReceivingYards = _sawRecYds ? _recYds : null,
                ReceivingTouchdowns = _sawRecTd ? _recTd : null,
                Fumbles = _sawFumbles ? _fumbles : null
            };

            var (completeness, missing) = StatsQuality.Evaluate(counting, _position);
            var (std, half, ppr) = LeagueFantasyScoring.CalculateAll(counting);

            return new PlayerSeasonStats
            {
                PlayerId = _playerId,
                Season = _season,
                SeasonType = "regular",
                Period = StatsPeriod.CompletedSeason,
                Level = FootballLevel.Nfl,
                Games = _games,
                Starts = null,
                PassAttempts = counting.PassAttempts,
                PassCompletions = counting.PassCompletions,
                PassYards = counting.PassYards,
                PassTouchdowns = counting.PassTouchdowns,
                PassInterceptions = counting.PassInterceptions,
                RushAttempts = counting.RushAttempts,
                RushYards = counting.RushYards,
                RushTouchdowns = counting.RushTouchdowns,
                Targets = counting.Targets,
                Receptions = counting.Receptions,
                ReceivingYards = counting.ReceivingYards,
                ReceivingTouchdowns = counting.ReceivingTouchdowns,
                Fumbles = counting.Fumbles,
                FantasyPointsStandard = std,
                FantasyPointsHalfPpr = half,
                FantasyPointsPpr = ppr,
                SourceProvider = "nflverse",
                Source = $"player_stats_{_season}.csv.gz",
                Completeness = completeness,
                IdentityMatch = StatsIdentityMatch.Matched,
                MissingFields = missing,
                LastUpdated = now
            };
        }
    }
}
