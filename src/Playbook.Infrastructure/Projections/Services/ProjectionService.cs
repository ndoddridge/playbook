using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Projections.Services;

/// <summary>
/// Builds and caches <see cref="PlayerProjection"/> values from
/// statistics + intelligence + optional matchup/environment + league context.
/// Invalidates automatically when the active league/team context changes.
/// </summary>
public sealed class ProjectionService : IGlobalProjectionService, IDisposable
{
    private readonly IProjectionEngine _engine;
    private readonly IPlayerProductionProvider _production;
    private readonly IIntelligenceService _intelligence;
    private readonly IPlayerInjuryService _injuries;
    private readonly IPlayerStatisticalContextService _statisticalContext;
    private readonly IMatchupContextProvider _matchups;
    private readonly IGameEnvironmentProvider _environments;
    private readonly IPlayerService _players;
    private readonly ILeagueState _leagueState;
    private readonly ProjectionSyncStatus _status;
    private readonly ILogger<ProjectionService> _logger;
    private readonly ProjectionCache _cache;
    private readonly object _gate = new();

    private IReadOnlyList<PlayerProjection> _projections = [];
    private int _loadedVersion;
    private Dictionary<Guid, PlayerProjection> _byPlayer = new();
    private bool _loaded;
    private Guid? _projectedLeagueId;
    private ScoringTypeSnapshot _scoringSnapshot;

    public ProjectionService(
        IProjectionEngine engine,
        IPlayerProductionProvider production,
        IIntelligenceService intelligence,
        IPlayerInjuryService injuries,
        IPlayerStatisticalContextService statisticalContext,
        IMatchupContextProvider matchups,
        IGameEnvironmentProvider environments,
        IPlayerService players,
        ILeagueState leagueState,
        ProjectionSyncStatus status,
        ILogger<ProjectionService> logger,
        ProjectionCache? cache = null)
    {
        _cache = cache ?? new ProjectionCache();
        _engine = engine;
        _production = production;
        _intelligence = intelligence;
        _injuries = injuries;
        _statisticalContext = statisticalContext;
        _matchups = matchups;
        _environments = environments;
        _players = players;
        _leagueState = leagueState;
        _status = status;
        _logger = logger;
        _leagueState.Changed += OnLeagueContextChanged;
    }

    private void OnLeagueContextChanged() => Invalidate();

    public void Dispose() => _leagueState.Changed -= OnLeagueContextChanged;

    public string EngineVersion => _engine.Version;

    public PlayerProjection? GetProjection(Guid playerId)
    {
        EnsureLoaded();
        return _byPlayer.TryGetValue(playerId, out var projection) ? projection : null;
    }

    public PlayerProjection? ProjectPlayer(Guid playerId) => GetProjection(playerId);

    public IReadOnlyList<PlayerProjection> GetAllProjections()
    {
        EnsureLoaded();
        return _projections;
    }

    public IReadOnlyList<PlayerProjection> GetAllProjections(ProjectionLeagueContext context) =>
        BuildProjections(context);

    public IReadOnlyList<PlayerProjection> GetTopProjections(int count = 8)
    {
        EnsureLoaded();
        return _projections
            .OrderByDescending(p => p.ProjectedFantasyPoints)
            .ThenByDescending(p => p.Confidence)
            .ThenBy(p => p.PlayerId)
            .Take(Math.Max(1, count))
            .ToList();
    }

    public PlayerProjectionComparison? ComparePlayers(Guid leftPlayerId, Guid rightPlayerId)
    {
        var left = ProjectPlayer(leftPlayerId);
        var right = ProjectPlayer(rightPlayerId);
        if (left is null || right is null)
        {
            return null;
        }

        var notes = new List<string>
        {
            $"Projected points delta: {left.ProjectedFantasyPoints - right.ProjectedFantasyPoints:+0.0;-0.0;0.0}",
            $"Confidence delta: {left.Confidence - right.Confidence:+0;-0;0}",
            $"Volatility delta: {left.Volatility - right.Volatility:+0;-0;0}",
            $"Scoring: {left.ScoringFormat} · Week {left.Week} · Engine v{left.ProjectionVersion}"
        };

        if (left.ProjectedFantasyPoints > right.ProjectedFantasyPoints)
        {
            notes.Add("Left projects higher median production in this league context.");
        }
        else if (left.ProjectedFantasyPoints < right.ProjectedFantasyPoints)
        {
            notes.Add("Right projects higher median production in this league context.");
        }
        else
        {
            notes.Add("Medians are equal — compare floor/ceiling and confidence.");
        }

        return new PlayerProjectionComparison
        {
            Left = left,
            Right = right,
            ComparisonNotes = notes
        };
    }

    public IReadOnlyList<PlayerProjection> ProjectRoster(IEnumerable<Guid> playerIds)
    {
        EnsureLoaded();
        var set = playerIds.ToHashSet();
        return _projections
            .Where(p => set.Contains(p.PlayerId))
            .OrderByDescending(p => p.ProjectedFantasyPoints)
            .ThenBy(p => p.PlayerId)
            .ToList();
    }

    public void Refresh()
    {
        _cache.BumpVersion();
        lock (_gate)
        {
            ProjectLocked();
            _loaded = true;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _loaded = false;
            _projections = [];
            _byPlayer = new Dictionary<Guid, PlayerProjection>();
            _projectedLeagueId = null;
            _scoringSnapshot = default;
        }
    }

    private void EnsureLoaded()
    {
        var league = _leagueState.CurrentLeague;
        var leagueId = league?.Id;
        var scoring = league?.ScoringType ?? Core.Leagues.ScoringType.Ppr;
        var week = league?.CurrentWeek ?? 1;

        if (_loaded &&
            _loadedVersion == _cache.Version &&
            _projectedLeagueId == leagueId &&
            _scoringSnapshot.ScoringType == scoring &&
            _scoringSnapshot.Week == week)
        {
            return;
        }

        lock (_gate)
        {
            if (_loaded &&
                _loadedVersion == _cache.Version &&
                _projectedLeagueId == leagueId &&
                _scoringSnapshot.ScoringType == scoring &&
                _scoringSnapshot.Week == week)
            {
                return;
            }

            ProjectLocked();
            _loaded = true;
        }
    }

    private void ProjectLocked()
    {
        var league = _leagueState.CurrentLeague;
        var context = ProjectionLeagueContext.FromLeague(league);
        var version = _cache.Version;
        var key = (context.LeagueId, context.ScoringType, context.CurrentWeek, context.Season, context.NumberOfTeams);
        IReadOnlyList<PlayerProjection> projections;
        if (_cache.TryGet(key, version, out var shared))
        {
            // Another user (or this one earlier) already projected this exact league context.
            projections = shared;
        }
        else
        {
            Stopwatch watch;
            (projections, watch) = BuildProjectionsTimed(context);
            _cache.Set(key, version, projections);
            RecordStatus(projections, watch);
        }

        _loadedVersion = version;
        _projections = projections;
        _byPlayer = projections.ToDictionary(p => p.PlayerId);
        _projectedLeagueId = context.LeagueId;
        _scoringSnapshot = new ScoringTypeSnapshot(context.ScoringType, context.CurrentWeek);
    }

    /// <summary>
    /// Pure computation for an explicit context — does not touch the cached ambient state, so it
    /// is safe to call for a context other than the currently connected league (e.g. a followed
    /// Sleeper draft's own scoring format) without disturbing <see cref="GetAllProjections()"/>.
    /// </summary>
    private IReadOnlyList<PlayerProjection> BuildProjections(ProjectionLeagueContext context)
    {
        var (projections, watch) = BuildProjectionsTimed(context);
        RecordStatus(projections, watch);
        return projections;
    }

    private (IReadOnlyList<PlayerProjection> Projections, Stopwatch Watch) BuildProjectionsTimed(
        ProjectionLeagueContext context)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var players = _players.GetAllPlayers();
            var profiles = _intelligence.GetAllProfiles()
                .ToDictionary(p => p.PlayerId);

            var production = players.ToDictionary(
                p => p.Id,
                p => _production.GetProduction(p));

            var currentInjuries = players
                .Select(p => _injuries.GetCurrentInjury(p.Id))
                .Where(r => r is not null)
                .ToDictionary(r => r!.PlayerId, r => r!);

            var statsContexts = new Dictionary<Guid, PlayerStatisticalContext>();
            foreach (var player in players)
            {
                var ctx = _statisticalContext.GetContext(player.Id);
                if (ctx is not null)
                {
                    statsContexts[player.Id] = ctx;
                }
            }

            var matchups = players.ToDictionary(
                p => p.Id,
                p => _matchups.GetMatchup(p, context));
            var environments = players.ToDictionary(
                p => p.Id,
                p => _environments.GetEnvironment(p, context));

            var projections = _engine.ProjectMany(
                players,
                production,
                profiles,
                context,
                currentInjuries,
                statsContexts,
                matchups,
                environments);
            watch.Stop();
            return (projections, watch);
        }
        catch (Exception ex)
        {
            watch.Stop();
            _status.RecordFailure(ex.Message);
            _logger.LogWarning(ex, "Projection pipeline failed");
            throw;
        }
    }

    private void RecordStatus(IReadOnlyList<PlayerProjection> projections, Stopwatch watch)
    {
        var avgConfidence = projections.Count == 0
            ? 0
            : projections.Average(p => p.Confidence);
        var avgVolatility = projections.Count == 0
            ? 0
            : projections.Average(p => p.Volatility);
        var avgProjection = projections.Count == 0
            ? 0
            : (double)projections.Average(p => p.ProjectedFantasyPoints);
        var uniqueValues = projections
            .Select(p => p.ProjectedFantasyPoints)
            .Distinct()
            .Count();

        _status.RecordSuccess(
            ProjectionEngineVersions.DisplayName,
            _engine.Version,
            projections.Count,
            uniqueValues,
            avgProjection,
            avgConfidence,
            avgVolatility,
            watch.Elapsed);

        _logger.LogInformation(
            "{Engine} v{Version}: {Players} players, avg {Avg:0.0} pts, conf {Conf:0.0}, vol {Vol:0.0} in {Ms} ms",
            ProjectionEngineVersions.DisplayName,
            _engine.Version,
            projections.Count,
            avgProjection,
            avgConfidence,
            avgVolatility,
            watch.ElapsedMilliseconds);
    }

    private readonly record struct ScoringTypeSnapshot(Core.Leagues.ScoringType ScoringType, int Week);
}

/// <summary>
/// Shared (singleton) store of computed projections per league context, so concurrent users in the
/// same scoring/week don't each recompute every player. <see cref="BumpVersion"/> (called on every
/// data refresh) invalidates all entries.
/// </summary>
public sealed class ProjectionCache
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        (Guid? LeagueId, Core.Leagues.ScoringType Scoring, int Week, int Season, int Teams),
        (int Version, IReadOnlyList<PlayerProjection> Projections)> _entries = new();

    private int _version;

    public int Version => Volatile.Read(ref _version);

    public void BumpVersion() => Interlocked.Increment(ref _version);

    internal bool TryGet(
        (Guid? LeagueId, Core.Leagues.ScoringType Scoring, int Week, int Season, int Teams) key,
        int version,
        out IReadOnlyList<PlayerProjection> projections)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.Version == version)
        {
            projections = entry.Projections;
            return true;
        }

        projections = [];
        return false;
    }

    internal void Set(
        (Guid? LeagueId, Core.Leagues.ScoringType Scoring, int Week, int Season, int Teams) key,
        int version,
        IReadOnlyList<PlayerProjection> projections)
    {
        if (_entries.Count > 64)
        {
            _entries.Clear();
        }

        _entries[key] = (version, projections);
    }
}
