using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.News;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Injury facade: current + optional NFL historical + college providers, plus unconfirmed news signals.
/// Never fabricates history when providers cannot supply it.
/// </summary>
public sealed class PlayerInjuryService : IPlayerInjuryService
{
    private readonly IPlayerInjuryProvider _primary;
    private readonly MockPlayerInjuryProvider _fallback;
    private readonly IHistoricalInjuryProvider _historical;
    private readonly ICollegeInjuryProvider _college;
    private readonly INewsProvider _news;
    private readonly InjuryCacheStore _cache;
    private readonly InjurySyncStatus _status;
    private readonly InjuryOptions _options;
    private readonly ILogger<PlayerInjuryService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<PlayerInjuryRecord> _currentRecords = [];
    private IReadOnlyList<PlayerInjuryRecord> _nflHistorical = [];
    private IReadOnlyList<PlayerInjuryRecord> _collegeHistorical = [];
    private InjuryProviderCapabilities _capabilities = InjuryProviderCapabilities.CurrentOnlyEspnSleeper;
    private HistoricalDataStatus _nflHistoricalStatus = HistoricalDataStatus.NotSynced;
    private HistoricalDataStatus _collegeHistoricalStatus = HistoricalDataStatus.NotSynced;
    private HistoricalDataStatus _globalHistoricalStatus = HistoricalDataStatus.NotSynced;
    private string _providerCoverage = "Not synced";
    private string _injuryProviders = "—";
    private bool _loaded;
    private bool _syncFailed;
    private DateTimeOffset? _lastUpdated;

    public PlayerInjuryService(
        IEnumerable<IPlayerInjuryProvider> providers,
        MockPlayerInjuryProvider fallback,
        IHistoricalInjuryProvider historical,
        ICollegeInjuryProvider college,
        INewsProvider news,
        InjuryCacheStore cache,
        InjurySyncStatus status,
        IOptions<InjuryOptions> options,
        ILogger<PlayerInjuryService> logger)
    {
        _fallback = fallback;
        _historical = historical;
        _college = college;
        _news = news;
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
            return _currentRecords.Concat(_nflHistorical).Concat(_collegeHistorical).ToList();
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

            current = current.Select(NormalizeCurrent).ToList();

            IReadOnlyList<PlayerInjuryRecord> nflHistorical = [];
            HistoricalDataStatus nflStatus;
            if (_historical.IsConfigured)
            {
                try
                {
                    nflHistorical = (await _historical.GetHistoricalInjuriesAsync(cancellationToken)
                            .ConfigureAwait(false))
                        .Select(r => NormalizeHistorical(r, InjuryCompetitionLevel.Nfl))
                        .ToList();
                    nflStatus = nflHistorical.Count > 0
                        ? HistoricalDataStatus.Available
                        : HistoricalDataStatus.NoRecordsFound;
                }
                catch (Exception ex)
                {
                    priorError = AppendError(priorError, $"NFL historical: {ex.Message}");
                    nflStatus = HistoricalDataStatus.Unavailable;
                    _logger.LogWarning(ex, "NFL historical injury provider failed");
                }
            }
            else if (capabilities.SupportsHistoricalInjuries)
            {
                nflHistorical = current.Where(r => !r.IsCurrent).Select(r =>
                    NormalizeHistorical(r, InjuryCompetitionLevel.Nfl)).ToList();
                current = current.Where(r => r.IsCurrent).ToList();
                nflStatus = nflHistorical.Count > 0
                    ? HistoricalDataStatus.Available
                    : HistoricalDataStatus.NoRecordsFound;
            }
            else
            {
                nflStatus = HistoricalDataStatus.NotSupportedByProvider;
            }

            IReadOnlyList<PlayerInjuryRecord> collegeHistorical = [];
            HistoricalDataStatus collegeStatus;
            if (_college.IsConfigured)
            {
                try
                {
                    collegeHistorical = (await _college.GetCollegeInjuriesAsync(cancellationToken)
                            .ConfigureAwait(false))
                        .Select(r => NormalizeHistorical(r, InjuryCompetitionLevel.College))
                        .ToList();
                    collegeStatus = collegeHistorical.Count > 0
                        ? HistoricalDataStatus.Available
                        : HistoricalDataStatus.NoRecordsFound;
                }
                catch (Exception ex)
                {
                    priorError = AppendError(priorError, $"College historical: {ex.Message}");
                    collegeStatus = HistoricalDataStatus.Unavailable;
                    _logger.LogWarning(ex, "College injury provider failed");
                }
            }
            else
            {
                collegeStatus = HistoricalDataStatus.NotSupportedByProvider;
            }

            var globalStatus = CombineHistoricalStatus(nflStatus, collegeStatus);
            var coverage = BuildCoverage(capabilities, nflStatus, collegeStatus);
            var providers = string.Join(" + ", new[]
            {
                active.ToString(),
                _historical.IsConfigured ? _historical.DisplayName : null,
                _college.IsConfigured ? _college.DisplayName : null
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            watch.Stop();
            Apply(current, nflHistorical, collegeHistorical, capabilities, nflStatus, collegeStatus,
                globalStatus, coverage, providers, syncFailed: false);
            _cache.Save(new InjuryCacheDocument
            {
                LastUpdatedUtc = DateTimeOffset.UtcNow,
                Provider = active.ToString(),
                Records = current.Concat(nflHistorical).Concat(collegeHistorical).ToList(),
                HistoricalDataStatus = globalStatus.ToString(),
                SupportsHistorical = _historical.IsConfigured || _college.IsConfigured ||
                                     capabilities.SupportsHistoricalInjuries
            });
            RecordTelemetry(active, watch.Elapsed, usedFallback, usedCache: false, priorError);
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
                List<PlayerInjuryRecord> nfl;
                List<PlayerInjuryRecord> college;
                HistoricalDataStatus nflStatus;
                HistoricalDataStatus collegeStatus;

                if (!supportsHistorical)
                {
                    nfl = [];
                    college = [];
                    nflStatus = HistoricalDataStatus.NotSupportedByProvider;
                    collegeStatus = HistoricalDataStatus.NotSupportedByProvider;
                    historicalStatus = HistoricalDataStatus.NotSupportedByProvider;
                    current = fresh.Records
                        .Select(NormalizeCurrent)
                        .GroupBy(r => r.PlayerId)
                        .Select(g => g.OrderByDescending(r => r.Date).First())
                        .ToList();
                }
                else
                {
                    current = fresh.Records.Where(r => r.IsCurrent).Select(NormalizeCurrent).ToList();
                    nfl = fresh.Records
                        .Where(r => !r.IsCurrent && r.Level != InjuryCompetitionLevel.College)
                        .Select(r => NormalizeHistorical(r, InjuryCompetitionLevel.Nfl))
                        .ToList();
                    college = fresh.Records
                        .Where(r => !r.IsCurrent && r.Level == InjuryCompetitionLevel.College)
                        .Select(r => NormalizeHistorical(r, InjuryCompetitionLevel.College))
                        .ToList();
                    nflStatus = nfl.Count > 0 ? HistoricalDataStatus.Available :
                        _historical.IsConfigured ? HistoricalDataStatus.NoRecordsFound :
                        HistoricalDataStatus.NotSupportedByProvider;
                    collegeStatus = college.Count > 0 ? HistoricalDataStatus.Available :
                        _college.IsConfigured ? HistoricalDataStatus.NoRecordsFound :
                        HistoricalDataStatus.NotSupportedByProvider;
                    historicalStatus = CombineHistoricalStatus(nflStatus, collegeStatus);
                }

                var capabilities = supportsHistorical
                    ? InjuryProviderCapabilities.MockWithHistory
                    : InjuryProviderCapabilities.CurrentOnlyEspnSleeper;
                var coverage = BuildCoverage(capabilities, nflStatus, collegeStatus);
                var providers = fresh.Provider;

                ApplyUnlocked(current, nfl, college, capabilities, nflStatus, collegeStatus,
                    historicalStatus, coverage, providers, syncFailed: false);
                _loaded = true;
                RecordTelemetry(
                    Enum.TryParse<InjuryProviderKind>(fresh.Provider, out var kind) ? kind : _primary.Kind,
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
        IReadOnlyList<PlayerInjuryRecord> nfl,
        IReadOnlyList<PlayerInjuryRecord> college,
        InjuryProviderCapabilities capabilities,
        HistoricalDataStatus nflStatus,
        HistoricalDataStatus collegeStatus,
        HistoricalDataStatus globalStatus,
        string coverage,
        string providers,
        bool syncFailed)
    {
        lock (_gate)
        {
            ApplyUnlocked(current, nfl, college, capabilities, nflStatus, collegeStatus,
                globalStatus, coverage, providers, syncFailed);
            _loaded = true;
        }
    }

    private void ApplyUnlocked(
        IReadOnlyList<PlayerInjuryRecord> current,
        IReadOnlyList<PlayerInjuryRecord> nfl,
        IReadOnlyList<PlayerInjuryRecord> college,
        InjuryProviderCapabilities capabilities,
        HistoricalDataStatus nflStatus,
        HistoricalDataStatus collegeStatus,
        HistoricalDataStatus globalStatus,
        string coverage,
        string providers,
        bool syncFailed)
    {
        _capabilities = capabilities;
        _nflHistoricalStatus = nflStatus;
        _collegeHistoricalStatus = collegeStatus;
        _globalHistoricalStatus = globalStatus;
        _providerCoverage = coverage;
        _injuryProviders = providers;
        _syncFailed = syncFailed;
        _lastUpdated = DateTimeOffset.UtcNow;
        _currentRecords = current.OrderBy(r => r.PlayerId).ThenByDescending(r => r.Date).ToList();
        _nflHistorical = nfl.OrderBy(r => r.PlayerId).ThenByDescending(r => r.Date).ToList();
        _collegeHistorical = college.OrderBy(r => r.PlayerId).ThenByDescending(r => r.Date).ToList();
    }

    private PlayerInjuryProfile BuildProfileUnlocked(Guid playerId)
    {
        var currentRows = _currentRecords.Where(r => r.PlayerId == playerId).ToList();
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

        var nflRows = _nflHistorical.Where(r => r.PlayerId == playerId).ToList();
        var collegeRows = _collegeHistorical.Where(r => r.PlayerId == playerId).ToList();
        var allHistorical = nflRows.Concat(collegeRows).ToList();
        var asOf = DateTimeOffset.UtcNow;
        var scored = InjuryRelevanceCalculator.ScoreAll(allHistorical, asOf);

        var nflStatus = ResolvePerPlayerStatus(_nflHistoricalStatus, nflRows.Count);
        var collegeStatus = ResolvePerPlayerStatus(_collegeHistoricalStatus, collegeRows.Count);
        var globalStatus = CombineHistoricalStatus(nflStatus, collegeStatus);

        var news = _news.GetForPlayer(playerId, 12);
        var unconfirmed = UnconfirmedInjurySignalExtractor.ExtractForPlayer(
            playerId,
            news,
            hasConfirmedCurrentInjury: currentStatus == CurrentInjuryDataStatus.Available);

        var sources = new List<string>();
        if (current?.Source is not null)
        {
            sources.Add(current.Source);
        }

        sources.AddRange(allHistorical.Select(r => r.Source).Where(s => !string.IsNullOrWhiteSpace(s))!);
        sources.AddRange(unconfirmed.Select(s => s.Source).Where(s => !string.IsNullOrWhiteSpace(s)));
        if (sources.Count == 0)
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
            RecentHistory = scored
                .Where(e => e.Band is InjuryRelevanceBand.High or InjuryRelevanceBand.Moderate)
                .ToList(),
            NflCareerHistory = scored
                .Where(e => e.Record.Level != InjuryCompetitionLevel.College)
                .ToList(),
            CollegeHistory = scored
                .Where(e => e.Record.Level == InjuryCompetitionLevel.College)
                .ToList(),
            HistoricalEntries = scored,
            HistoricalRecords = allHistorical.OrderByDescending(r => r.Date).ToList(),
            HistoricalDataStatus = globalStatus,
            NflHistoricalDataStatus = nflStatus,
            CollegeHistoricalDataStatus = collegeStatus,
            UnconfirmedSignals = unconfirmed,
            RiskSummary = BuildRiskSummary(currentStatus, current, globalStatus, scored, unconfirmed),
            LastUpdated = current?.LastUpdated ?? _lastUpdated,
            SupportingSources = sources.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            HistoricalAvailabilityMessage = InjuryAvailabilityPresentation.HistoricalMessage(nflStatus),
            CollegeAvailabilityMessage = InjuryAvailabilityPresentation.CollegeMessage(collegeStatus),
            ProviderCoverage = _providerCoverage
        };
    }

    private static HistoricalDataStatus ResolvePerPlayerStatus(HistoricalDataStatus global, int count)
    {
        if (global is HistoricalDataStatus.NotSupportedByProvider
            or HistoricalDataStatus.Unavailable
            or HistoricalDataStatus.NotSynced)
        {
            return global;
        }

        return count > 0 ? HistoricalDataStatus.Available : HistoricalDataStatus.NoRecordsFound;
    }

    private static HistoricalDataStatus CombineHistoricalStatus(
        HistoricalDataStatus nfl,
        HistoricalDataStatus college)
    {
        if (nfl == HistoricalDataStatus.Available || college == HistoricalDataStatus.Available)
        {
            return HistoricalDataStatus.Available;
        }

        if (nfl == HistoricalDataStatus.Unavailable || college == HistoricalDataStatus.Unavailable)
        {
            return HistoricalDataStatus.Unavailable;
        }

        if (nfl == HistoricalDataStatus.NotSupportedByProvider &&
            college == HistoricalDataStatus.NotSupportedByProvider)
        {
            return HistoricalDataStatus.NotSupportedByProvider;
        }

        if (nfl == HistoricalDataStatus.NoRecordsFound || college == HistoricalDataStatus.NoRecordsFound)
        {
            return HistoricalDataStatus.NoRecordsFound;
        }

        return nfl;
    }

    private static string BuildCoverage(
        InjuryProviderCapabilities capabilities,
        HistoricalDataStatus nfl,
        HistoricalDataStatus college)
    {
        var parts = new List<string>
        {
            capabilities.SupportsCurrentInjuries ? "Current: yes" : "Current: no",
            nfl == HistoricalDataStatus.NotSupportedByProvider ? "NFL history: not supported" :
            nfl == HistoricalDataStatus.Available ? "NFL history: available" :
            nfl == HistoricalDataStatus.NoRecordsFound ? "NFL history: synced (no rows)" :
            $"NFL history: {nfl}",
            college == HistoricalDataStatus.NotSupportedByProvider ? "College history: not supported" :
            college == HistoricalDataStatus.Available ? "College history: available" :
            college == HistoricalDataStatus.NoRecordsFound ? "College history: synced (no rows)" :
            $"College history: {college}"
        };
        return string.Join(" · ", parts);
    }

    private static PlayerInjuryRecord NormalizeCurrent(PlayerInjuryRecord r) =>
        r with
        {
            IsCurrent = true,
            Verified = true,
            Level = r.Level ?? InjuryCompetitionLevel.Nfl,
            Severity = r.Severity ?? InjurySeverityInference.FromStatus(r.Status, r.GamesMissed)
        };

    private static PlayerInjuryRecord NormalizeHistorical(
        PlayerInjuryRecord r,
        InjuryCompetitionLevel level) =>
        r with
        {
            IsCurrent = false,
            Verified = true,
            Level = r.Level ?? level,
            Severity = r.Severity ?? InjurySeverityInference.FromStatus(r.Status, r.GamesMissed)
        };

    private static bool IsBenignClearance(PlayerInjuryRecord record) =>
        record.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
        record.Status.Equals("Healthy", StringComparison.OrdinalIgnoreCase);

    private static string BuildRiskSummary(
        CurrentInjuryDataStatus currentStatus,
        PlayerInjuryRecord? current,
        HistoricalDataStatus historicalStatus,
        IReadOnlyList<InjuryHistoryEntry> scored,
        IReadOnlyList<UnconfirmedInjurySignal> unconfirmed)
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

        if (unconfirmed.Count > 0)
        {
            var top = unconfirmed[0];
            return $"No confirmed current injury. Possible injury concern — unconfirmed ({top.ConfidenceLabel} confidence).";
        }

        var highHist = scored.FirstOrDefault(e => e.Band == InjuryRelevanceBand.High);
        if (highHist is not null)
        {
            return "No current designation; recent/high-relevance historical injury context is available.";
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
        TimeSpan runtime,
        bool usedFallback,
        bool usedCache,
        string? priorError)
    {
        var current = _currentRecords;
        var nfl = _nflHistorical;
        var college = _collegeHistorical;
        var historical = nfl.Concat(college).ToList();
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

        // Count unconfirmed across a sample of players with news (cheap: latest news only).
        var unconfirmedCount = 0;
        try
        {
            var articles = _news.GetLatest(40);
            var playerIds = articles.SelectMany(a => a.RelatedPlayerIds).Distinct().Take(80);
            foreach (var id in playerIds)
            {
                var hasCurrent = current.Any(r => r.PlayerId == id && !IsBenignClearance(r));
                unconfirmedCount += UnconfirmedInjurySignalExtractor
                    .ExtractForPlayer(id, articles.Where(a => a.RelatedPlayerIds.Contains(id)), hasCurrent)
                    .Count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unconfirmed injury telemetry skipped");
        }

        _status.RecordSuccess(
            active,
            _injuryProviders,
            _providerCoverage,
            playersWithData,
            playersWithCurrent,
            playersWithHistorical,
            current.Count + historical.Count,
            current.Count,
            historical.Count,
            nfl.Count,
            college.Count,
            unconfirmedCount,
            _globalHistoricalStatus,
            _historical.IsConfigured || _college.IsConfigured || _capabilities.SupportsHistoricalInjuries,
            runtime,
            usedFallback,
            usedCache,
            priorError);
    }

    private static string AppendError(string? prior, string next) =>
        string.IsNullOrWhiteSpace(prior) ? next : $"{prior}; {next}";
}
