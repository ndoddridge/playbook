using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Injury facade: current provider + optional historical provider, with honest availability status.
/// Does not fabricate history when the active provider cannot supply it.
/// </summary>
public sealed class PlayerInjuryService : IPlayerInjuryService
{
    private readonly IPlayerInjuryProvider _primary;
    private readonly MockPlayerInjuryProvider _fallback;
    private readonly IHistoricalInjuryProvider _historical;
    private readonly InjuryCacheStore _cache;
    private readonly InjurySyncStatus _status;
    private readonly InjuryOptions _options;
    private readonly ILogger<PlayerInjuryService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<PlayerInjuryRecord> _currentRecords = [];
    private IReadOnlyList<PlayerInjuryRecord> _historicalRecords = [];
    private Dictionary<Guid, List<PlayerInjuryRecord>> _currentByPlayer = new();
    private Dictionary<Guid, List<PlayerInjuryRecord>> _historicalByPlayer = new();
    private InjuryProviderCapabilities _capabilities = InjuryProviderCapabilities.CurrentOnlyEspnSleeper;
    private HistoricalDataStatus _globalHistoricalStatus = HistoricalDataStatus.NotSynced;
    private bool _loaded;
    private bool _syncFailed;
    private DateTimeOffset? _lastUpdated;

    public PlayerInjuryService(
        IEnumerable<IPlayerInjuryProvider> providers,
        MockPlayerInjuryProvider fallback,
        IHistoricalInjuryProvider historical,
        InjuryCacheStore cache,
        InjurySyncStatus status,
        IOptions<InjuryOptions> options,
        ILogger<PlayerInjuryService> logger)
    {
        _fallback = fallback;
        _historical = historical;
        _cache = cache;
        _status = status;
        _options = options.Value;
        _logger = logger;

        var configured = _options.Provider;
        _status.SetConfigured(configured);
        _primary = providers.FirstOrDefault(p => p.Kind == configured) ?? fallback;
        _capabilities = _primary.Capabilities;
    }

    public InjuryProviderCapabilities ActiveCapabilities
    {
        get
        {
            EnsureLoaded();
            lock (_gate)
            {
                return _capabilities;
            }
        }
    }

    public HistoricalDataStatus GlobalHistoricalDataStatus
    {
        get
        {
            EnsureLoaded();
            lock (_gate)
            {
                return _globalHistoricalStatus;
            }
        }
    }

    public IReadOnlyList<PlayerInjuryRecord> GetAllInjuries()
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _currentRecords.Concat(_historicalRecords).ToList();
        }
    }

    public IReadOnlyList<PlayerInjuryRecord> GetInjuriesForPlayer(Guid playerId)
    {
        var profile = GetPlayerInjuryProfile(playerId);
        var rows = new List<PlayerInjuryRecord>();
        if (profile.CurrentInjury is not null)
        {
            rows.Add(profile.CurrentInjury);
        }

        rows.AddRange(profile.HistoricalRecords);
        return rows;
    }

    public PlayerInjuryRecord? GetCurrentInjury(Guid playerId) =>
        GetPlayerInjuryProfile(playerId).CurrentInjury;

    public IReadOnlyList<PlayerInjuryRecord> GetHistoricalInjuries(Guid playerId) =>
        GetPlayerInjuryProfile(playerId).HistoricalRecords;

    public PlayerInjuryProfile GetPlayerInjuryProfile(Guid playerId)
    {
        EnsureLoaded();
        lock (_gate)
        {
            return BuildProfileUnlocked(playerId);
        }
    }

    public void Refresh() =>
        RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            string? priorError = null;
            var usedFallback = false;
            IReadOnlyList<PlayerInjuryRecord> current;
            InjuryProviderKind active;
            InjuryProviderCapabilities capabilities;

            try
            {
                current = await _primary.GetInjuriesAsync(cancellationToken).ConfigureAwait(false);
                active = _primary.Kind;
                capabilities = _primary.Capabilities;
            }
            catch (Exception ex) when (_primary.Kind != InjuryProviderKind.Mock)
            {
                priorError = ex.Message;
                _logger.LogWarning(ex, "Live injury provider failed; falling back to mock");
                current = await _fallback.GetInjuriesAsync(cancellationToken).ConfigureAwait(false);
                active = InjuryProviderKind.Mock;
                capabilities = _fallback.Capabilities;
                usedFallback = true;
            }

            IReadOnlyList<PlayerInjuryRecord> historical = [];
            HistoricalDataStatus historicalStatus;

            if (!capabilities.SupportsHistoricalInjuries && !_historical.IsConfigured)
            {
                historicalStatus = HistoricalDataStatus.NotSupportedByProvider;
            }
            else if (_historical.IsConfigured)
            {
                try
                {
                    historical = await _historical.GetHistoricalInjuriesAsync(cancellationToken)
                        .ConfigureAwait(false);
                    historicalStatus = historical.Count > 0
                        ? HistoricalDataStatus.Available
                        : HistoricalDataStatus.NoRecordsFound;
                }
                catch (Exception ex)
                {
                    priorError = string.IsNullOrWhiteSpace(priorError)
                        ? $"Historical: {ex.Message}"
                        : $"{priorError}; Historical: {ex.Message}";
                    historicalStatus = HistoricalDataStatus.Unavailable;
                    _logger.LogWarning(ex, "Historical injury provider failed");
                }
            }
            else if (capabilities.SupportsHistoricalInjuries)
            {
                // Current provider claims history but none separated — treat non-current rows if any.
                historical = current.Where(r => !r.IsCurrent).ToList();
                current = current.Where(r => r.IsCurrent).ToList();
                historicalStatus = historical.Count > 0
                    ? HistoricalDataStatus.Available
                    : HistoricalDataStatus.NoRecordsFound;
            }
            else
            {
                historicalStatus = HistoricalDataStatus.NotSupportedByProvider;
            }

            // Current rows are always treated as current designations from the live/mock feed.
            current = current.Select(r => r with { IsCurrent = true }).ToList();
            historical = historical.Select(r => r with { IsCurrent = false }).ToList();

            watch.Stop();
            Apply(current, historical, capabilities, historicalStatus, syncFailed: false);
            _cache.Save(new InjuryCacheDocument
            {
                LastUpdatedUtc = DateTimeOffset.UtcNow,
                Provider = active.ToString(),
                Records = current.Concat(historical).ToList(),
                HistoricalDataStatus = historicalStatus.ToString(),
                SupportsHistorical = capabilities.SupportsHistoricalInjuries || _historical.IsConfigured
            });
            RecordTelemetry(active, capabilities, historicalStatus, watch.Elapsed, usedFallback, usedCache: false, priorError);
        }
        catch (Exception ex)
        {
            watch.Stop();
            _syncFailed = true;
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

            if (_cache.TryLoadFresh(out var fresh) && fresh.Records.Count >= 0)
            {
                var supportsHistorical = fresh.SupportsHistorical;
                var historicalStatus = Enum.TryParse<HistoricalDataStatus>(fresh.HistoricalDataStatus, out var parsed)
                    ? parsed
                    : supportsHistorical
                        ? HistoricalDataStatus.NoRecordsFound
                        : HistoricalDataStatus.NotSupportedByProvider;

                List<PlayerInjuryRecord> current;
                List<PlayerInjuryRecord> historical;
                if (!supportsHistorical)
                {
                    // Current-only providers: never treat cached rows as career history.
                    historical = [];
                    historicalStatus = HistoricalDataStatus.NotSupportedByProvider;
                    current = fresh.Records
                        .Select(r => r with { IsCurrent = true })
                        .GroupBy(r => r.PlayerId)
                        .Select(g => g.OrderByDescending(r => r.Date).First())
                        .ToList();
                }
                else
                {
                    current = fresh.Records.Where(r => r.IsCurrent).ToList();
                    historical = fresh.Records.Where(r => !r.IsCurrent).ToList();
                }

                var capabilities = supportsHistorical
                    ? InjuryProviderCapabilities.MockWithHistory
                    : InjuryProviderCapabilities.CurrentOnlyEspnSleeper;

                ApplyUnlocked(current, historical, capabilities, historicalStatus, syncFailed: false);
                _loaded = true;
                RecordTelemetry(
                    Enum.TryParse<InjuryProviderKind>(fresh.Provider, out var kind) ? kind : _primary.Kind,
                    capabilities,
                    historicalStatus,
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

    private void Apply(
        IReadOnlyList<PlayerInjuryRecord> current,
        IReadOnlyList<PlayerInjuryRecord> historical,
        InjuryProviderCapabilities capabilities,
        HistoricalDataStatus historicalStatus,
        bool syncFailed)
    {
        lock (_gate)
        {
            ApplyUnlocked(current, historical, capabilities, historicalStatus, syncFailed);
            _loaded = true;
        }
    }

    private void ApplyUnlocked(
        IReadOnlyList<PlayerInjuryRecord> current,
        IReadOnlyList<PlayerInjuryRecord> historical,
        InjuryProviderCapabilities capabilities,
        HistoricalDataStatus historicalStatus,
        bool syncFailed)
    {
        _capabilities = capabilities;
        _globalHistoricalStatus = historicalStatus;
        _syncFailed = syncFailed;
        _lastUpdated = DateTimeOffset.UtcNow;
        _currentRecords = current.OrderBy(r => r.PlayerId).ThenByDescending(r => r.Date).ToList();
        _historicalRecords = historical.OrderBy(r => r.PlayerId).ThenByDescending(r => r.Date).ToList();
        _currentByPlayer = _currentRecords.GroupBy(r => r.PlayerId).ToDictionary(g => g.Key, g => g.ToList());
        _historicalByPlayer = _historicalRecords.GroupBy(r => r.PlayerId).ToDictionary(g => g.Key, g => g.ToList());
    }

    private PlayerInjuryProfile BuildProfileUnlocked(Guid playerId)
    {
        _currentByPlayer.TryGetValue(playerId, out var currentRows);
        currentRows ??= [];
        var current = currentRows
            .OrderByDescending(r => r.Date)
            .FirstOrDefault(r => !IsBenignClearance(r))
            ?? currentRows.OrderByDescending(r => r.Date).FirstOrDefault();

        CurrentInjuryDataStatus currentStatus;
        if (_syncFailed && _currentRecords.Count == 0)
        {
            currentStatus = CurrentInjuryDataStatus.Unavailable;
        }
        else if (!_loaded)
        {
            currentStatus = CurrentInjuryDataStatus.NotSynced;
        }
        else if (current is null)
        {
            currentStatus = CurrentInjuryDataStatus.NoCurrentInjury;
        }
        else
        {
            currentStatus = CurrentInjuryDataStatus.Available;
        }

        IReadOnlyList<PlayerInjuryRecord> historicalRows = [];
        var historicalStatus = _globalHistoricalStatus;
        if (_globalHistoricalStatus == HistoricalDataStatus.Available ||
            _globalHistoricalStatus == HistoricalDataStatus.NoRecordsFound)
        {
            _historicalByPlayer.TryGetValue(playerId, out var hist);
            historicalRows = hist ?? [];
            historicalStatus = historicalRows.Count > 0
                ? HistoricalDataStatus.Available
                : HistoricalDataStatus.NoRecordsFound;
        }

        var sources = new List<string>();
        if (current?.Source is not null)
        {
            sources.Add(current.Source);
        }

        foreach (var src in historicalRows.Select(r => r.Source).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
        {
            sources.Add(src!);
        }

        if (sources.Count == 0 && _capabilities.Notes is not null)
        {
            sources.Add(_primary.DisplayName);
        }

        return new PlayerInjuryProfile
        {
            PlayerId = playerId,
            CurrentDataStatus = currentStatus,
            CurrentStatus = current?.Status,
            CurrentInjury = current,
            PracticeStatus = current?.PracticeStatus,
            GameStatus = current?.GameStatus,
            HistoricalRecords = historicalRows.OrderByDescending(r => r.Date).ToList(),
            HistoricalDataStatus = historicalStatus,
            RiskSummary = BuildRiskSummary(currentStatus, current, historicalStatus, historicalRows),
            LastUpdated = current?.LastUpdated ?? _lastUpdated,
            SupportingSources = sources.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            HistoricalAvailabilityMessage = InjuryAvailabilityPresentation.HistoricalMessage(historicalStatus)
        };
    }

    private static bool IsBenignClearance(PlayerInjuryRecord record)
    {
        // Prefer meaningful designations over "Active" noise when both exist.
        return record.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
               record.Status.Equals("Healthy", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRiskSummary(
        CurrentInjuryDataStatus currentStatus,
        PlayerInjuryRecord? current,
        HistoricalDataStatus historicalStatus,
        IReadOnlyList<PlayerInjuryRecord> historical)
    {
        if (currentStatus == CurrentInjuryDataStatus.Available && current is not null)
        {
            var rule = InjuryIntelligenceMapping.ResolveRuleId(current);
            return rule switch
            {
                "injury-out" or "injury-ir" => $"Major current availability risk ({current.Status}).",
                "injury-doubtful" or "injury-questionable" or "injury-limited" =>
                    $"Elevated current health uncertainty ({current.Status}).",
                "injury-positive" => "Positive recovery signal on the current report.",
                _ => $"Current designation: {current.Status}."
            };
        }

        if (historicalStatus == HistoricalDataStatus.Available && historical.Count > 0)
        {
            return "No current designation; historical injury context is available from the historical provider.";
        }

        if (historicalStatus == HistoricalDataStatus.NotSupportedByProvider)
        {
            return "No current designation. Historical injury data is not supported by the configured provider — absence does not imply a clean injury history.";
        }

        if (historicalStatus == HistoricalDataStatus.Unavailable)
        {
            return "No current designation. Historical injury data is temporarily unavailable.";
        }

        if (currentStatus == CurrentInjuryDataStatus.Unavailable)
        {
            return "Current injury data is unavailable.";
        }

        return "No current injury designation from the provider.";
    }

    private void RecordTelemetry(
        InjuryProviderKind active,
        InjuryProviderCapabilities capabilities,
        HistoricalDataStatus historicalStatus,
        TimeSpan runtime,
        bool usedFallback,
        bool usedCache,
        string? priorError)
    {
        var current = _currentRecords;
        var historical = _historicalRecords;
        var playersWithCurrent = current
            .Where(r => !IsBenignClearance(r))
            .Select(r => r.PlayerId)
            .Distinct()
            .Count();
        var playersWithHistorical = historical.Select(r => r.PlayerId).Distinct().Count();
        var playersWithData = current.Select(r => r.PlayerId)
            .Concat(historical.Select(r => r.PlayerId))
            .Distinct()
            .Count();

        _status.RecordSuccess(
            active,
            playersWithData,
            playersWithCurrent,
            playersWithHistorical,
            current.Count + historical.Count,
            current.Count,
            historical.Count,
            historicalStatus,
            capabilities.SupportsHistoricalInjuries || _historical.IsConfigured,
            runtime,
            usedFallback,
            usedCache,
            priorError);
    }
}
