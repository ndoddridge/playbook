using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Stats facade: configured provider with mock fallback, JSON cache, and query helpers.
/// </summary>
public sealed class PlayerStatsService : IPlayerStatsService
{
    private readonly IPlayerStatsProvider _primary;
    private readonly MockPlayerStatsProvider _fallback;
    private readonly PlayerStatsCacheStore _cache;
    private readonly PlayerStatsSyncStatus _status;
    private readonly PlayerStatsOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlayerStatsService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<PlayerSeasonStats> _records = [];
    private Dictionary<Guid, List<PlayerSeasonStats>> _byPlayer = new();
    private bool _loaded;

    public PlayerStatsService(
        IEnumerable<IPlayerStatsProvider> providers,
        MockPlayerStatsProvider fallback,
        PlayerStatsCacheStore cache,
        PlayerStatsSyncStatus status,
        IOptions<PlayerStatsOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<PlayerStatsService> logger)
    {
        _fallback = fallback;
        _cache = cache;
        _status = status;
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var configured = _options.Provider;
        _status.SetConfigured(configured);
        _primary = providers.FirstOrDefault(p => p.Kind == configured) ?? fallback;
    }

    public IReadOnlyList<PlayerSeasonStats> GetAllStats()
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _records;
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

    public IReadOnlyList<int> GetAvailableSeasons(Guid playerId) =>
        GetStatsForPlayer(playerId)
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

    public PlayerSeasonStats? GetPrimaryProductionSeason(Guid playerId)
    {
        var rows = GetStatsForPlayer(playerId)
            .Where(r => r.Period != StatsPeriod.College && r.HasAnyCountingStat)
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
            .Where(r => r.Period != StatsPeriod.College && r.HasAnyCountingStat)
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

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var request = await BuildSyncRequestAsync(cancellationToken).ConfigureAwait(false);
            string? priorError = null;
            var usedFallback = false;
            IReadOnlyList<PlayerSeasonStats> records;
            PlayerStatsProviderKind active;

            try
            {
                records = await _primary.GetSeasonStatsAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                active = _primary.Kind;
            }
            catch (Exception ex) when (_primary.Kind != PlayerStatsProviderKind.Mock)
            {
                priorError = ex.Message;
                _logger.LogWarning(ex, "Live stats provider failed; falling back to mock");
                records = await _fallback.GetSeasonStatsAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                active = PlayerStatsProviderKind.Mock;
                usedFallback = true;
            }

            watch.Stop();
            ApplyRecords(records);
            PersistCache(records, request, active);
            RecordTelemetry(active, records, request, watch.Elapsed, usedFallback, usedCache: false, priorError);
        }
        catch (Exception ex)
        {
            watch.Stop();
            _status.RecordFailure(ex.Message);
            _logger.LogWarning(ex, "Player stats refresh failed");
            throw;
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
                ApplyRecordsUnlocked(fresh.Records);
                _loaded = true;
                RecordTelemetry(
                    Enum.TryParse<PlayerStatsProviderKind>(fresh.Provider, out var kind)
                        ? kind
                        : _primary.Kind,
                    fresh.Records,
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

    private void ApplyRecords(IReadOnlyList<PlayerSeasonStats> records)
    {
        lock (_gate)
        {
            ApplyRecordsUnlocked(records);
            _loaded = true;
        }
    }

    private void ApplyRecordsUnlocked(IReadOnlyList<PlayerSeasonStats> records)
    {
        _records = records
            .OrderBy(r => r.PlayerId)
            .ThenByDescending(r => r.Season)
            .ThenBy(r => r.Period)
            .ToList();
        _byPlayer = _records
            .GroupBy(r => r.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private void PersistCache(
        IReadOnlyList<PlayerSeasonStats> records,
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
            Records = records.ToList()
        });
    }

    private void RecordTelemetry(
        PlayerStatsProviderKind active,
        IReadOnlyList<PlayerSeasonStats> records,
        PlayerStatsSyncRequest request,
        TimeSpan runtime,
        bool usedFallback,
        bool usedCache,
        string? priorError)
    {
        var players = records.Select(r => r.PlayerId).Distinct().Count();
        var current = records.Count(r => r.Period == StatsPeriod.CurrentSeason);
        var historical = records.Count(r => r.Period == StatsPeriod.CompletedSeason);
        var college = records.Count(r => r.Period == StatsPeriod.College);
        var seasons = records.Select(r => r.Season).Distinct().Count();

        _status.RecordSuccess(
            active,
            players,
            seasons,
            current,
            historical,
            college,
            runtime,
            usedFallback,
            usedCache,
            priorError);
    }

    private async Task<PlayerStatsSyncRequest> BuildSyncRequestAsync(CancellationToken cancellationToken)
    {
        var (current, previous, seasonType) = await ResolveNflStateAsync(cancellationToken)
            .ConfigureAwait(false);

        var historicalCount = Math.Clamp(_options.HistoricalSeasonCount, 1, 10);
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
