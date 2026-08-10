using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Statistics sync pipeline:
/// initial historical import → incremental / current-season updates → normalization → storage →
/// intelligence invalidation. UI and Intelligence consume this store, not provider DTOs.
/// </summary>
public sealed class PlayerStatsService : IPlayerStatsService
{
    private readonly IPlayerStatsProvider _primary;
    private readonly MockPlayerStatsProvider _fallback;
    private readonly IHistoricalPlayerStatsProvider _historical;
    private readonly PlayerStatsCacheStore _cache;
    private readonly PlayerGameLogCacheStore _gameLogCache;
    private readonly PlayerStatsSyncStatus _status;
    private readonly PlayerStatsOptions _options;
    private readonly CollegeStatsService _collegeStats;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _services;
    private readonly ILogger<PlayerStatsService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<PlayerSeasonStats> _records = [];
    private IReadOnlyList<PlayerGameStats> _gameLogs = [];
    private Dictionary<Guid, List<PlayerSeasonStats>> _byPlayer = new();
    private Dictionary<Guid, List<PlayerGameStats>> _gamesByPlayer = new();
    private bool _loaded;
    private int _identityMatches;
    private int _unresolvedPlayers;

    public PlayerStatsService(
        IEnumerable<IPlayerStatsProvider> providers,
        MockPlayerStatsProvider fallback,
        IHistoricalPlayerStatsProvider historical,
        PlayerStatsCacheStore cache,
        PlayerGameLogCacheStore gameLogCache,
        PlayerStatsSyncStatus status,
        IOptions<PlayerStatsOptions> options,
        CollegeStatsService collegeStats,
        IHttpClientFactory httpClientFactory,
        IServiceProvider services,
        ILogger<PlayerStatsService> logger)
    {
        _fallback = fallback;
        _historical = historical;
        _cache = cache;
        _gameLogCache = gameLogCache;
        _status = status;
        _options = options.Value;
        _collegeStats = collegeStats;
        _httpClientFactory = httpClientFactory;
        _services = services;
        _logger = logger;

        var configured = _options.Provider;
        _status.SetConfigured(configured, _options.HistoricalProvider);
        _primary = providers.FirstOrDefault(p => p.Kind == configured) ?? fallback;
    }

    public int GameLogCount
    {
        get
        {
            EnsureLoaded();
            lock (_gate)
            {
                return _gameLogs.Count;
            }
        }
    }

    public IReadOnlyList<PlayerSeasonStats> GetAllStats()
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _records;
        }
    }

    public IReadOnlyList<PlayerGameStats> GetAllGameLogs()
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _gameLogs;
        }
    }

    public IReadOnlyList<PlayerSeasonStats> GetStatsForPlayer(Guid playerId)
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _byPlayer.TryGetValue(playerId, out var rows) ? rows : [];
        }
    }

    public IReadOnlyList<PlayerGameStats> GetGameLogsForPlayer(Guid playerId)
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _gamesByPlayer.TryGetValue(playerId, out var rows) ? rows : [];
        }
    }

    public IReadOnlyList<PlayerGameStats> GetRecentGameLogs(Guid playerId, int maxGames = 8) =>
        GetGameLogsForPlayer(playerId)
            .OrderByDescending(g => g.Season)
            .ThenByDescending(g => g.Week)
            .Take(Math.Max(1, maxGames))
            .ToList();

    public IReadOnlyList<int> GetAvailableSeasons(Guid playerId) =>
        GetStatsForPlayer(playerId)
            .Where(r => r.Period is StatsPeriod.CompletedSeason or StatsPeriod.CurrentSeason or StatsPeriod.College)
            .Select(r => r.Season)
            .Distinct()
            .OrderByDescending(s => s)
            .ToList();

    public PlayerSeasonStats? GetStats(Guid playerId, int season, StatsPeriod? period = null)
    {
        var rows = GetStatsForPlayer(playerId).Where(r => r.Season == season);
        if (period is not null)
        {
            rows = rows.Where(r => r.Period == period);
        }

        return rows
            .OrderByDescending(r => r.Period == StatsPeriod.CurrentSeason)
            .ThenByDescending(r => r.Games ?? 0)
            .FirstOrDefault();
    }

    public PlayerSeasonStats? GetCareerTotals(Guid playerId) =>
        GetStatsForPlayer(playerId).FirstOrDefault(r => r.Period == StatsPeriod.Career);

    public PlayerSeasonStats? GetPrimaryProductionSeason(Guid playerId)
    {
        var rows = GetStatsForPlayer(playerId)
            .Where(r => r.Period is StatsPeriod.CompletedSeason or StatsPeriod.CurrentSeason)
            .Where(r => r.Level == FootballLevel.Nfl && r.HasAnyCountingStat)
            .ToList();
        if (rows.Count == 0)
        {
            return null;
        }

        var current = rows.FirstOrDefault(r => r.Period == StatsPeriod.CurrentSeason);
        if (current is not null && (current.Games ?? 0) >= 4)
        {
            return current;
        }

        return rows
            .Where(r => r.Period == StatsPeriod.CompletedSeason)
            .OrderByDescending(r => r.Season)
            .ThenByDescending(r => r.Games ?? 0)
            .FirstOrDefault()
            ?? current;
    }

    public IReadOnlyList<PlayerSeasonStats> GetRecentNflSeasons(Guid playerId, int maxSeasons = 3) =>
        GetStatsForPlayer(playerId)
            .Where(r => r.Period is StatsPeriod.CompletedSeason or StatsPeriod.CurrentSeason)
            .Where(r => r.Level == FootballLevel.Nfl && r.HasAnyCountingStat)
            .GroupBy(r => r.Season)
            .Select(g => g
                .OrderByDescending(r => r.Period == StatsPeriod.CurrentSeason)
                .ThenByDescending(r => r.Games ?? 0)
                .First())
            .OrderByDescending(r => r.Season)
            .Take(Math.Max(1, maxSeasons))
            .ToList();

    public void Refresh() =>
        RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();

    public Task RefreshCurrentSeasonAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(cancellationToken, currentSeasonOnly: true);

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(cancellationToken, currentSeasonOnly: false);

    private async Task RefreshAsync(CancellationToken cancellationToken, bool currentSeasonOnly)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var request = await BuildSyncRequestAsync(cancellationToken).ConfigureAwait(false);
            string? priorError = null;
            var usedFallback = false;
            IReadOnlyList<PlayerSeasonStats> providerSeasons;
            PlayerStatsProviderKind active;

            try
            {
                if (currentSeasonOnly)
                {
                    providerSeasons = await _primary.GetSeasonStatsAsync(
                            new PlayerStatsSyncRequest
                            {
                                CurrentSeason = request.CurrentSeason,
                                CompletedSeasons = [],
                                SeasonType = request.SeasonType
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    providerSeasons = await _primary.GetSeasonStatsAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                }

                active = _primary.Kind;
            }
            catch (Exception ex) when (_primary.Kind != PlayerStatsProviderKind.Mock)
            {
                priorError = ex.Message;
                _logger.LogWarning(ex, "Live stats provider failed; falling back to mock");
                providerSeasons = await _fallback.GetSeasonStatsAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                active = PlayerStatsProviderKind.Mock;
                usedFallback = true;
            }

            IReadOnlyList<PlayerSeasonStats> historicalSeasons = [];
            IReadOnlyList<PlayerGameStats> gameLogs = [];
            var identityMatches = 0;
            var unresolved = 0;

            if (_historical.IsConfigured &&
                (_options.Provider == PlayerStatsProviderKind.Live ||
                 _historical.Kind == HistoricalPlayerStatsProviderKind.Mock))
            {
                try
                {
                    var histSeasons = currentSeasonOnly
                        ? []
                        : request.CompletedSeasons
                            .Take(Math.Clamp(_options.HistoricalSeasonCount, 1, 15))
                            .ToList();

                    // Always refresh the newest completed season when doing current-only? skip.
                    // For full sync, pull historical seasons; game-log retention may be a subset.
                    if (!currentSeasonOnly || histSeasons.Count > 0)
                    {
                        var histRequest = new HistoricalPlayerStatsSyncRequest
                        {
                            Seasons = histSeasons,
                            SeasonType = request.SeasonType,
                            ForceRedownload = false
                        };

                        if (histRequest.Seasons.Count > 0)
                        {
                            var batch = await _historical.GetHistoricalStatsAsync(histRequest, cancellationToken)
                                .ConfigureAwait(false);
                            historicalSeasons = batch.SeasonRecords.ToList();
                            // Retain the newest seasons that actually produced game logs (skip missing nflverse years).
                            var maxLogSeasons = Math.Clamp(_options.GameLogSeasonCount, 1, 10);
                            var availableLogSeasons = batch.GameLogs
                                .Select(g => g.Season)
                                .Distinct()
                                .OrderByDescending(s => s)
                                .Take(maxLogSeasons)
                                .ToHashSet();
                            gameLogs = batch.GameLogs
                                .Where(g => availableLogSeasons.Contains(g.Season))
                                .ToList();
                            identityMatches = batch.IdentityMatches;
                            unresolved = batch.UnresolvedPlayers;
                            if (!string.IsNullOrWhiteSpace(batch.Error))
                            {
                                priorError = string.IsNullOrWhiteSpace(priorError)
                                    ? batch.Error
                                    : $"{priorError}; {batch.Error}";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    priorError = string.IsNullOrWhiteSpace(priorError)
                        ? $"Historical: {ex.Message}"
                        : $"{priorError}; Historical: {ex.Message}";
                    _logger.LogWarning(ex, "Historical stats provider failed; continuing with season provider");
                }
            }

            if (currentSeasonOnly && _gameLogCache.TryLoad(out var priorGames))
            {
                gameLogs = priorGames.GameLogs;
            }

            // Incremental current-season updates must retain previously loaded historical rows.
            if (currentSeasonOnly)
            {
                IReadOnlyList<PlayerSeasonStats> prior;
                lock (_gate)
                {
                    prior = _records;
                }

                if (prior.Count == 0 && _cache.TryLoadAny() is { Records.Count: > 0 } cached)
                {
                    prior = cached.Records;
                }

                var retained = prior
                    .Where(r => r.Period is not StatsPeriod.CurrentSeason and not StatsPeriod.Career)
                    .ToList();
                providerSeasons = retained
                    .Concat(providerSeasons.Where(r => r.Period == StatsPeriod.CurrentSeason))
                    .ToList();
            }

            var nflMerged = MergeNflSeasons(providerSeasons, historicalSeasons, request.CurrentSeason);
            var withCareer = AppendCareerTotals(nflMerged);

            var collegeCached = _collegeStats.GetCachedOrEmpty();
            var records = MergeRecords(withCareer, collegeCached);
            ApplyRecords(records, gameLogs, identityMatches, unresolved);
            PersistCache(records, gameLogs, request, active);

            try
            {
                if (!currentSeasonOnly)
                {
                    var collegeRecords = await _collegeStats.RefreshAsync(cancellationToken)
                        .ConfigureAwait(false);
                    records = MergeRecords(withCareer, collegeRecords);
                    ApplyRecords(records, gameLogs, identityMatches, unresolved);
                    PersistCache(records, gameLogs, request, active);
                }
            }
            catch (Exception ex)
            {
                priorError = string.IsNullOrWhiteSpace(priorError)
                    ? $"College: {ex.Message}"
                    : $"{priorError}; College: {ex.Message}";
                _logger.LogWarning(ex, "College stats refresh failed; continuing with NFL stats only");
            }

            watch.Stop();
            RecordTelemetry(
                active,
                records,
                gameLogs,
                identityMatches,
                unresolved,
                request,
                watch.Elapsed,
                usedFallback,
                usedCache: false,
                priorError);

            InvalidateIntelligence();
        }
        catch (Exception ex)
        {
            watch.Stop();
            _status.RecordFailure(ex.Message);
            _logger.LogWarning(ex, "Player stats refresh failed");
            throw;
        }
    }

    private void InvalidateIntelligence()
    {
        try
        {
            var intelligence = _services.GetService(typeof(IIntelligenceService)) as IIntelligenceService;
            intelligence?.Refresh();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Intelligence invalidation after stats sync failed (non-fatal)");
        }
    }

    private void EnsureLoaded()
    {
        if (Volatile.Read(ref _loaded))
        {
            return;
        }

        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            if (_cache.TryLoadFresh(out var fresh))
            {
                var college = _collegeStats.GetCachedOrEmpty();
                var merged = MergeRecords(fresh.Records, college);
                IReadOnlyList<PlayerGameStats> games = [];
                if (_gameLogCache.TryLoad(out var gameDoc))
                {
                    games = gameDoc.GameLogs;
                }

                ApplyRecordsUnlocked(merged, games, fresh.IdentityMatches, fresh.UnresolvedPlayers);
                _loaded = true;
                RecordTelemetry(
                    Enum.TryParse<PlayerStatsProviderKind>(fresh.Provider, out var kind)
                        ? kind
                        : _primary.Kind,
                    merged,
                    games,
                    fresh.IdentityMatches,
                    fresh.UnresolvedPlayers,
                    new PlayerStatsSyncRequest
                    {
                        CurrentSeason = fresh.CurrentSeason,
                        CompletedSeasons = fresh.Seasons.Where(s => s != fresh.CurrentSeason).ToList(),
                        SeasonType = "regular"
                    },
                    TimeSpan.Zero,
                    usedFallback: false,
                    usedCache: true,
                    priorError: null);
                return;
            }
        }

        Refresh();
        lock (_gate)
        {
            _loaded = true;
        }
    }

    private static IReadOnlyList<PlayerSeasonStats> MergeNflSeasons(
        IReadOnlyList<PlayerSeasonStats> providerRows,
        IReadOnlyList<PlayerSeasonStats> historicalRows,
        int currentSeason)
    {
        // Prefer nflverse historical for completed seasons (richer + game-log aligned).
        // Prefer primary provider for current season.
        var map = new Dictionary<(Guid Id, int Season, StatsPeriod Period), PlayerSeasonStats>();

        foreach (var row in historicalRows.Where(r => r.Period != StatsPeriod.College))
        {
            var period = row.Season == currentSeason ? StatsPeriod.CurrentSeason : StatsPeriod.CompletedSeason;
            var normalized = CloneWithPeriod(row, period);
            map[(normalized.PlayerId, normalized.Season, normalized.Period)] = normalized;
        }

        foreach (var row in providerRows.Where(r => r.Period != StatsPeriod.College))
        {
            var key = (row.PlayerId, row.Season, row.Period);
            if (row.Period == StatsPeriod.CurrentSeason || !map.ContainsKey((row.PlayerId, row.Season, StatsPeriod.CompletedSeason)))
            {
                map[key] = EnrichProviderRow(row);
            }
            else if (row.Period == StatsPeriod.CompletedSeason &&
                     !map.ContainsKey((row.PlayerId, row.Season, StatsPeriod.CompletedSeason)))
            {
                map[key] = EnrichProviderRow(row);
            }
        }

        return map.Values.ToList();
    }

    private static PlayerSeasonStats EnrichProviderRow(PlayerSeasonStats row)
    {
        var counting = row.ToCountingStats();
        var (completeness, missing) = StatsQuality.Evaluate(counting, null);
        var (std, half, ppr) = LeagueFantasyScoring.CalculateAll(counting);
        return new PlayerSeasonStats
        {
            PlayerId = row.PlayerId,
            Season = row.Season,
            SeasonType = row.SeasonType,
            Period = row.Period,
            Level = row.Period == StatsPeriod.College ? FootballLevel.College : FootballLevel.Nfl,
            Games = row.Games,
            Starts = row.Starts,
            PassAttempts = row.PassAttempts,
            PassCompletions = row.PassCompletions,
            PassYards = row.PassYards,
            PassTouchdowns = row.PassTouchdowns,
            PassInterceptions = row.PassInterceptions,
            RushAttempts = row.RushAttempts,
            RushYards = row.RushYards,
            RushTouchdowns = row.RushTouchdowns,
            Targets = row.Targets,
            Receptions = row.Receptions,
            ReceivingYards = row.ReceivingYards,
            ReceivingTouchdowns = row.ReceivingTouchdowns,
            Fumbles = row.Fumbles,
            FantasyPointsStandard = row.FantasyPointsStandard ?? std,
            FantasyPointsHalfPpr = row.FantasyPointsHalfPpr ?? half,
            FantasyPointsPpr = row.FantasyPointsPpr ?? ppr,
            CollegeSchool = row.CollegeSchool,
            SourceProvider = row.SourceProvider,
            Source = row.Source ?? row.SourceProvider,
            Completeness = completeness,
            IdentityMatch = row.IdentityMatch == default ? StatsIdentityMatch.Matched : row.IdentityMatch,
            MissingFields = missing.Count > 0 ? missing : row.MissingFields,
            LastUpdated = row.LastUpdated
        };
    }

    private static PlayerSeasonStats CloneWithPeriod(PlayerSeasonStats row, StatsPeriod period) =>
        new()
        {
            PlayerId = row.PlayerId,
            Season = row.Season,
            SeasonType = row.SeasonType,
            Period = period,
            Level = FootballLevel.Nfl,
            Games = row.Games,
            Starts = row.Starts,
            PassAttempts = row.PassAttempts,
            PassCompletions = row.PassCompletions,
            PassYards = row.PassYards,
            PassTouchdowns = row.PassTouchdowns,
            PassInterceptions = row.PassInterceptions,
            RushAttempts = row.RushAttempts,
            RushYards = row.RushYards,
            RushTouchdowns = row.RushTouchdowns,
            Targets = row.Targets,
            Receptions = row.Receptions,
            ReceivingYards = row.ReceivingYards,
            ReceivingTouchdowns = row.ReceivingTouchdowns,
            Fumbles = row.Fumbles,
            FantasyPointsStandard = row.FantasyPointsStandard,
            FantasyPointsHalfPpr = row.FantasyPointsHalfPpr,
            FantasyPointsPpr = row.FantasyPointsPpr,
            SourceProvider = row.SourceProvider,
            Source = row.Source,
            Completeness = row.Completeness,
            IdentityMatch = row.IdentityMatch,
            MissingFields = row.MissingFields,
            LastUpdated = row.LastUpdated
        };

    private static IReadOnlyList<PlayerSeasonStats> AppendCareerTotals(
        IReadOnlyList<PlayerSeasonStats> nflRows)
    {
        var result = new List<PlayerSeasonStats>(nflRows);
        var groups = nflRows
            .Where(r => r.Level == FootballLevel.Nfl &&
                        r.Period is StatsPeriod.CompletedSeason or StatsPeriod.CurrentSeason)
            .GroupBy(r => r.PlayerId);

        foreach (var group in groups)
        {
            var seasons = group
                .GroupBy(r => r.Season)
                .Select(g => g
                    .OrderByDescending(r => r.Period == StatsPeriod.CurrentSeason)
                    .First())
                .ToList();
            if (seasons.Count == 0)
            {
                continue;
            }

            var counting = new CanonicalCountingStats
            {
                PassAttempts = Sum(seasons.Select(s => s.PassAttempts)),
                PassCompletions = Sum(seasons.Select(s => s.PassCompletions)),
                PassYards = Sum(seasons.Select(s => s.PassYards)),
                PassTouchdowns = Sum(seasons.Select(s => s.PassTouchdowns)),
                PassInterceptions = Sum(seasons.Select(s => s.PassInterceptions)),
                RushAttempts = Sum(seasons.Select(s => s.RushAttempts)),
                RushYards = Sum(seasons.Select(s => s.RushYards)),
                RushTouchdowns = Sum(seasons.Select(s => s.RushTouchdowns)),
                Targets = Sum(seasons.Select(s => s.Targets)),
                Receptions = Sum(seasons.Select(s => s.Receptions)),
                ReceivingYards = Sum(seasons.Select(s => s.ReceivingYards)),
                ReceivingTouchdowns = Sum(seasons.Select(s => s.ReceivingTouchdowns)),
                Fumbles = Sum(seasons.Select(s => s.Fumbles))
            };
            var (std, half, ppr) = LeagueFantasyScoring.CalculateAll(counting);
            var now = DateTimeOffset.UtcNow;
            result.Add(new PlayerSeasonStats
            {
                PlayerId = group.Key,
                Season = seasons.Max(s => s.Season),
                SeasonType = "career",
                Period = StatsPeriod.Career,
                Level = FootballLevel.Career,
                Games = Sum(seasons.Select(s => s.Games)),
                Starts = Sum(seasons.Select(s => s.Starts)),
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
                SourceProvider = "Playbook",
                Source = "nfl-career-aggregate",
                Completeness = StatsCompleteness.Partial,
                IdentityMatch = StatsIdentityMatch.Matched,
                MissingFields = counting.ListMissingCoreFields(),
                LastUpdated = now
            });
        }

        return result;
    }

    private static int? Sum(IEnumerable<int?> values)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return list.Count == 0 ? null : list.Sum();
    }

    private static IReadOnlyList<PlayerSeasonStats> MergeRecords(
        IReadOnlyList<PlayerSeasonStats> nfl,
        IReadOnlyList<PlayerSeasonStats> college)
    {
        var rows = new List<PlayerSeasonStats>(nfl.Count + college.Count);
        rows.AddRange(nfl.Where(r => r.Period != StatsPeriod.College && r.Level != FootballLevel.College));
        rows.AddRange(college
            .Where(r => r.Period == StatsPeriod.College && r.HasAnyCountingStat)
            .Select(r => new PlayerSeasonStats
            {
                PlayerId = r.PlayerId,
                Season = r.Season,
                SeasonType = r.SeasonType,
                Period = StatsPeriod.College,
                Level = FootballLevel.College,
                Games = r.Games,
                Starts = r.Starts,
                PassAttempts = r.PassAttempts,
                PassCompletions = r.PassCompletions,
                PassYards = r.PassYards,
                PassTouchdowns = r.PassTouchdowns,
                PassInterceptions = r.PassInterceptions,
                RushAttempts = r.RushAttempts,
                RushYards = r.RushYards,
                RushTouchdowns = r.RushTouchdowns,
                Targets = r.Targets,
                Receptions = r.Receptions,
                ReceivingYards = r.ReceivingYards,
                ReceivingTouchdowns = r.ReceivingTouchdowns,
                Fumbles = r.Fumbles,
                FantasyPointsStandard = r.FantasyPointsStandard,
                FantasyPointsHalfPpr = r.FantasyPointsHalfPpr,
                FantasyPointsPpr = r.FantasyPointsPpr,
                CollegeSchool = r.CollegeSchool,
                SourceProvider = r.SourceProvider,
                Source = r.Source ?? r.SourceProvider,
                Completeness = r.Completeness,
                IdentityMatch = r.IdentityMatch,
                MissingFields = r.MissingFields,
                LastUpdated = r.LastUpdated
            }));
        return rows;
    }

    private void ApplyRecords(
        IReadOnlyList<PlayerSeasonStats> records,
        IReadOnlyList<PlayerGameStats> gameLogs,
        int identityMatches,
        int unresolved)
    {
        lock (_gate)
        {
            ApplyRecordsUnlocked(records, gameLogs, identityMatches, unresolved);
            _loaded = true;
        }
    }

    private void ApplyRecordsUnlocked(
        IReadOnlyList<PlayerSeasonStats> records,
        IReadOnlyList<PlayerGameStats> gameLogs,
        int identityMatches,
        int unresolved)
    {
        _records = records
            .OrderBy(r => r.PlayerId)
            .ThenByDescending(r => r.Season)
            .ThenBy(r => r.Period)
            .ToList();
        _byPlayer = _records
            .GroupBy(r => r.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());
        _gameLogs = gameLogs
            .OrderBy(g => g.PlayerId)
            .ThenByDescending(g => g.Season)
            .ThenByDescending(g => g.Week)
            .ToList();
        _gamesByPlayer = _gameLogs
            .GroupBy(g => g.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());
        _identityMatches = identityMatches;
        _unresolvedPlayers = unresolved;
    }

    private void PersistCache(
        IReadOnlyList<PlayerSeasonStats> records,
        IReadOnlyList<PlayerGameStats> gameLogs,
        PlayerStatsSyncRequest request,
        PlayerStatsProviderKind active)
    {
        var seasons = request.CompletedSeasons
            .Append(request.CurrentSeason)
            .Distinct()
            .OrderByDescending(s => s)
            .ToList();

        _cache.Save(new PlayerStatsCacheDocument
        {
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            Provider = active.ToString(),
            CurrentSeason = request.CurrentSeason,
            Seasons = seasons,
            Records = records.ToList(),
            IdentityMatches = _identityMatches,
            UnresolvedPlayers = _unresolvedPlayers
        });

        _gameLogCache.Save(new PlayerGameLogCacheDocument
        {
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            Provider = _historical.DisplayName,
            Seasons = gameLogs.Select(g => g.Season).Distinct().OrderByDescending(s => s).ToList(),
            GameLogs = gameLogs.ToList()
        });
    }

    private void RecordTelemetry(
        PlayerStatsProviderKind active,
        IReadOnlyList<PlayerSeasonStats> records,
        IReadOnlyList<PlayerGameStats> gameLogs,
        int identityMatches,
        int unresolved,
        PlayerStatsSyncRequest request,
        TimeSpan runtime,
        bool usedFallback,
        bool usedCache,
        string? priorError)
    {
        var nflRows = records.Where(r => r.Level == FootballLevel.Nfl).ToList();
        var players = records.Select(r => r.PlayerId).Distinct().Count();
        var nflPlayers = nflRows.Select(r => r.PlayerId).Distinct().Count();
        var current = records.Count(r => r.Period == StatsPeriod.CurrentSeason);
        var historical = records.Count(r => r.Period == StatsPeriod.CompletedSeason);
        var college = records.Count(r => r.Period == StatsPeriod.College);
        var seasons = records
            .Where(r => r.Period is StatsPeriod.CompletedSeason or StatsPeriod.CurrentSeason or StatsPeriod.College)
            .Select(r => r.Season)
            .Distinct()
            .Count();
        var nflSeasons = nflRows
            .Where(r => r.Period is StatsPeriod.CompletedSeason or StatsPeriod.CurrentSeason)
            .Select(r => r.Season)
            .Distinct()
            .Count();

        var providers = $"{active}" +
                        (_historical.IsConfigured ? $" + {_historical.Kind}" : string.Empty) +
                        " + College";

        _status.RecordSuccess(
            active,
            providers,
            players,
            nflPlayers,
            seasons,
            nflSeasons,
            current,
            historical,
            college,
            gameLogs.Count,
            identityMatches,
            unresolved,
            runtime,
            usedFallback,
            usedCache,
            priorError);
    }

    private async Task<PlayerStatsSyncRequest> BuildSyncRequestAsync(CancellationToken cancellationToken)
    {
        var (current, previous, seasonType) = await ResolveNflStateAsync(cancellationToken)
            .ConfigureAwait(false);

        var historicalCount = Math.Clamp(_options.HistoricalSeasonCount, 1, 15);
        var completed = new List<int>();
        for (var i = 0; i < historicalCount; i++)
        {
            completed.Add(previous - i);
        }

        foreach (var required in new[] { 2023, 2024, 2025 })
        {
            if (required < current && !completed.Contains(required))
            {
                completed.Add(required);
            }
        }

        return new PlayerStatsSyncRequest
        {
            CurrentSeason = current,
            CompletedSeasons = completed.Distinct().OrderByDescending(s => s).ToList(),
            SeasonType = seasonType
        };
    }

    private async Task<(int Current, int Previous, string SeasonType)> ResolveNflStateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(LivePlayerStatsProvider.HttpClientName);
            var state = await client.GetFromJsonAsync<SleeperNflState>("state/nfl", cancellationToken)
                .ConfigureAwait(false);
            if (state is not null &&
                int.TryParse(state.Season, out var current) &&
                int.TryParse(state.PreviousSeason, out var previous))
            {
                var seasonType = string.IsNullOrWhiteSpace(state.SeasonType)
                    ? "regular"
                    : state.SeasonType!;
                return (current, previous, seasonType is "pre" ? "regular" : seasonType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to resolve NFL state; using calendar defaults");
        }

        var year = DateTime.UtcNow.Year;
        return (year, year - 1, "regular");
    }

    private sealed class SleeperNflState
    {
        [JsonPropertyName("season")]
        public string? Season { get; set; }

        [JsonPropertyName("previous_season")]
        public string? PreviousSeason { get; set; }

        [JsonPropertyName("season_type")]
        public string? SeasonType { get; set; }
    }
}
