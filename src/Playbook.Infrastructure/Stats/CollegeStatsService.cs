using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Players;
using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Players;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Facade for college statistics: configured provider, mock fallback, and dedicated cache.
/// </summary>
public sealed class CollegeStatsService
{
    private readonly ICollegeStatsProvider _primary;
    private readonly MockCollegeStatsProvider _fallback;
    private readonly CollegeStatsCacheStore _cache;
    private readonly CollegeStatsSyncStatus _status;
    private readonly CollegeStatsOptions _options;
    private readonly IPlayerService _players;
    private readonly ILogger<CollegeStatsService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<PlayerSeasonStats> _records = [];

    public CollegeStatsService(
        IEnumerable<ICollegeStatsProvider> providers,
        MockCollegeStatsProvider fallback,
        CollegeStatsCacheStore cache,
        CollegeStatsSyncStatus status,
        IOptions<CollegeStatsOptions> options,
        IPlayerService players,
        ILogger<CollegeStatsService> logger)
    {
        _fallback = fallback;
        _cache = cache;
        _status = status;
        _options = options.Value;
        _players = players;
        _logger = logger;

        var configured = _options.Provider;
        _status.SetConfigured(configured);
        _primary = providers.FirstOrDefault(p => p.Kind == configured) ?? fallback;
    }

    public IReadOnlyList<PlayerSeasonStats> GetCachedOrEmpty()
    {
        lock (_gate)
        {
            if (_records.Count > 0)
            {
                return _records;
            }
        }

        if (_cache.TryLoadFresh(out var fresh))
        {
            Apply(fresh.Records);
            _status.RecordSuccess(
                Enum.TryParse<CollegeStatsProviderKind>(fresh.Provider, out var kind)
                    ? kind
                    : _primary.Kind,
                fresh.Records.Select(r => r.PlayerId).Distinct().Count(),
                fresh.Records.Count,
                TimeSpan.Zero,
                usedFallback: false,
                usedCache: true,
                priorError: null);
            return fresh.Records;
        }

        return [];
    }

    public async Task<IReadOnlyList<PlayerSeasonStats>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryLoadFresh(out var fresh) && fresh.Records.Count > 0)
        {
            Apply(fresh.Records);
            _status.RecordSuccess(
                Enum.TryParse<CollegeStatsProviderKind>(fresh.Provider, out var kind)
                    ? kind
                    : _primary.Kind,
                fresh.Records.Select(r => r.PlayerId).Distinct().Count(),
                fresh.Records.Count,
                TimeSpan.Zero,
                usedFallback: false,
                usedCache: true,
                priorError: null);
            return fresh.Records;
        }

        var watch = Stopwatch.StartNew();
        string? priorError = null;
        var usedFallback = false;
        IReadOnlyList<PlayerSeasonStats> records;
        CollegeStatsProviderKind active;

        var request = new CollegeStatsSyncRequest
        {
            Candidates = BuildCandidates()
        };

        try
        {
            records = await _primary.GetCollegeStatsAsync(request, cancellationToken)
                .ConfigureAwait(false);
            active = _primary.Kind;
        }
        catch (Exception ex) when (_primary.Kind != CollegeStatsProviderKind.Mock)
        {
            priorError = ex.Message;
            _logger.LogWarning(ex, "Live college stats provider failed; falling back to mock");
            records = await _fallback.GetCollegeStatsAsync(request, cancellationToken)
                .ConfigureAwait(false);
            active = CollegeStatsProviderKind.Mock;
            usedFallback = true;
        }

        watch.Stop();
        Apply(records);

        // Avoid caching an empty live miss for a full TTL — that would hide later successes.
        if (records.Count > 0 || active == CollegeStatsProviderKind.Mock)
        {
            _cache.Save(new CollegeStatsCacheDocument
            {
                LastUpdatedUtc = DateTimeOffset.UtcNow,
                Provider = active.ToString(),
                Records = records.ToList()
            });
        }

        _status.RecordSuccess(
            active,
            records.Select(r => r.PlayerId).Distinct().Count(),
            records.Count,
            watch.Elapsed,
            usedFallback,
            usedCache: false,
            priorError);
        return records;
    }

    private void Apply(IReadOnlyList<PlayerSeasonStats> records)
    {
        lock (_gate)
        {
            _records = records
                .Where(r => r.Period == StatsPeriod.College)
                .OrderBy(r => r.PlayerId)
                .ThenByDescending(r => r.Season)
                .ToList();
        }
    }

    private IReadOnlyList<CollegePlayerCandidate> BuildCandidates()
    {
        IReadOnlyList<Player> players;
        try
        {
            players = _players.GetAllPlayers();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to load players for college stats candidates");
            return [];
        }

        return players
            .Where(p => p.Position is Position.QB or Position.RB or Position.WR or Position.TE)
            .Where(p => (p.YearsPro ?? 0) < 3)
            .Select(p => new CollegePlayerCandidate
            {
                PlayerId = p.Id,
                FullName = p.FullName,
                FirstName = p.FirstName,
                LastName = p.LastName,
                Team = p.Team,
                College = p.College,
                YearsPro = p.YearsPro,
                EspnAthleteId = null
            })
            .ToList();
    }
}
