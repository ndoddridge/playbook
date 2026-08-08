using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Injury facade: configured provider with mock fallback, historical merge cache, query helpers.
/// </summary>
public sealed class PlayerInjuryService : IPlayerInjuryService
{
    private readonly IPlayerInjuryProvider _primary;
    private readonly MockPlayerInjuryProvider _fallback;
    private readonly InjuryCacheStore _cache;
    private readonly InjurySyncStatus _status;
    private readonly InjuryOptions _options;
    private readonly ILogger<PlayerInjuryService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<PlayerInjuryRecord> _records = [];
    private Dictionary<Guid, List<PlayerInjuryRecord>> _byPlayer = new();
    private bool _loaded;

    public PlayerInjuryService(
        IEnumerable<IPlayerInjuryProvider> providers,
        MockPlayerInjuryProvider fallback,
        InjuryCacheStore cache,
        InjurySyncStatus status,
        IOptions<InjuryOptions> options,
        ILogger<PlayerInjuryService> logger)
    {
        _fallback = fallback;
        _cache = cache;
        _status = status;
        _options = options.Value;
        _logger = logger;

        var configured = _options.Provider;
        _status.SetConfigured(configured);
        _primary = providers.FirstOrDefault(p => p.Kind == configured) ?? fallback;
    }

    public IReadOnlyList<PlayerInjuryRecord> GetAllInjuries()
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _records;
        }
    }

    public IReadOnlyList<PlayerInjuryRecord> GetInjuriesForPlayer(Guid playerId)
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _byPlayer.TryGetValue(playerId, out var rows) ? rows : [];
        }
    }

    public PlayerInjuryRecord? GetCurrentInjury(Guid playerId) =>
        GetInjuriesForPlayer(playerId).FirstOrDefault(r => r.IsCurrent)
        ?? GetInjuriesForPlayer(playerId).OrderByDescending(r => r.Date).FirstOrDefault();

    public IReadOnlyList<PlayerInjuryRecord> GetHistoricalInjuries(Guid playerId) =>
        GetInjuriesForPlayer(playerId)
            .Where(r => !r.IsCurrent)
            .OrderByDescending(r => r.Date)
            .ToList();

    public void Refresh() =>
        RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            string? priorError = null;
            var usedFallback = false;
            IReadOnlyList<PlayerInjuryRecord> incoming;
            InjuryProviderKind active;

            try
            {
                incoming = await _primary.GetInjuriesAsync(cancellationToken).ConfigureAwait(false);
                active = _primary.Kind;
            }
            catch (Exception ex) when (_primary.Kind != InjuryProviderKind.Mock)
            {
                priorError = ex.Message;
                _logger.LogWarning(ex, "Live injury provider failed; falling back to mock");
                incoming = await _fallback.GetInjuriesAsync(cancellationToken).ConfigureAwait(false);
                active = InjuryProviderKind.Mock;
                usedFallback = true;
            }

            var previous = _cache.TryLoadAny()?.Records ?? [];
            var merged = MergeHistory(previous, incoming);
            watch.Stop();
            Apply(merged);
            _cache.Save(new InjuryCacheDocument
            {
                LastUpdatedUtc = DateTimeOffset.UtcNow,
                Provider = active.ToString(),
                Records = merged.ToList()
            });
            RecordTelemetry(active, merged, watch.Elapsed, usedFallback, usedCache: false, priorError);
        }
        catch (Exception ex)
        {
            watch.Stop();
            _status.RecordFailure(ex.Message);
            _logger.LogWarning(ex, "Injury refresh failed");
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

            if (_cache.TryLoadFresh(out var fresh) && fresh.Records.Count > 0)
            {
                ApplyUnlocked(fresh.Records);
                _loaded = true;
                RecordTelemetry(
                    Enum.TryParse<InjuryProviderKind>(fresh.Provider, out var kind) ? kind : _primary.Kind,
                    fresh.Records,
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

    private void Apply(IReadOnlyList<PlayerInjuryRecord> records)
    {
        lock (_gate)
        {
            ApplyUnlocked(records);
            _loaded = true;
        }
    }

    private void ApplyUnlocked(IReadOnlyList<PlayerInjuryRecord> records)
    {
        _records = records
            .OrderBy(r => r.PlayerId)
            .ThenByDescending(r => r.Date)
            .ToList();
        _byPlayer = _records
            .GroupBy(r => r.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Preserve prior records and mark only the freshest designation per player as current.
    /// Incoming live rows replace same ExternalId; otherwise they are appended.
    /// </summary>
    public static IReadOnlyList<PlayerInjuryRecord> MergeHistory(
        IReadOnlyList<PlayerInjuryRecord> previous,
        IReadOnlyList<PlayerInjuryRecord> incoming)
    {
        var byKey = new Dictionary<string, PlayerInjuryRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in previous)
        {
            byKey[HistoryKey(row)] = row with { IsCurrent = false };
        }

        foreach (var row in incoming)
        {
            byKey[HistoryKey(row)] = row with { IsCurrent = true };
        }

        var grouped = byKey.Values
            .GroupBy(r => r.PlayerId)
            .SelectMany(g =>
            {
                var ordered = g.OrderByDescending(r => r.Date).ThenByDescending(r => r.LastUpdated).ToList();
                var latestKey = HistoryKey(ordered[0]);
                return ordered.Select(r => r with { IsCurrent = HistoryKey(r) == latestKey });
            })
            .OrderBy(r => r.PlayerId)
            .ThenByDescending(r => r.Date)
            .ToList();

        return grouped;
    }

    private static string HistoryKey(PlayerInjuryRecord row) =>
        !string.IsNullOrWhiteSpace(row.ExternalId)
            ? row.ExternalId!
            : $"{row.PlayerId:N}|{row.Date:yyyy-MM-dd}|{row.Status}|{row.BodyPart}";

    private void RecordTelemetry(
        InjuryProviderKind active,
        IReadOnlyList<PlayerInjuryRecord> records,
        TimeSpan runtime,
        bool usedFallback,
        bool usedCache,
        string? priorError)
    {
        var players = records.Select(r => r.PlayerId).Distinct().Count();
        var current = records.Count(r => r.IsCurrent);
        var historical = records.Count(r => !r.IsCurrent);
        _status.RecordSuccess(
            active,
            players,
            records.Count,
            current,
            historical,
            runtime,
            usedFallback,
            usedCache,
            priorError);
    }
}
