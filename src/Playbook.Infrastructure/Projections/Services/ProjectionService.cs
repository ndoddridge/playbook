using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Projections.Services;

/// <summary>
/// Builds and caches <see cref="PlayerProjection"/> values from intelligence + league context.
/// </summary>
public sealed class ProjectionService : IProjectionService
{
    private readonly IProjectionEngine _engine;
    private readonly IIntelligenceService _intelligence;
    private readonly IPlayerService _players;
    private readonly ILeagueState _leagueState;
    private readonly ProjectionSyncStatus _status;
    private readonly ILogger<ProjectionService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<PlayerProjection> _projections = [];
    private Dictionary<Guid, PlayerProjection> _byPlayer = new();
    private bool _loaded;
    private Guid? _projectedLeagueId;
    private ScoringTypeSnapshot _scoringSnapshot;

    public ProjectionService(
        IProjectionEngine engine,
        IIntelligenceService intelligence,
        IPlayerService players,
        ILeagueState leagueState,
        ProjectionSyncStatus status,
        ILogger<ProjectionService> logger)
    {
        _engine = engine;
        _intelligence = intelligence;
        _players = players;
        _leagueState = leagueState;
        _status = status;
        _logger = logger;
    }

    public PlayerProjection? GetProjection(Guid playerId)
    {
        EnsureLoaded();
        return _byPlayer.TryGetValue(playerId, out var projection) ? projection : null;
    }

    public IReadOnlyList<PlayerProjection> GetAllProjections()
    {
        EnsureLoaded();
        return _projections;
    }

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

    public void Refresh()
    {
        lock (_gate)
        {
            ProjectLocked();
            _loaded = true;
        }
    }

    private void EnsureLoaded()
    {
        var league = _leagueState.CurrentLeague;
        var leagueId = league?.Id;
        var scoring = league?.ScoringType ?? Core.Leagues.ScoringType.Ppr;
        var week = league?.CurrentWeek ?? 1;

        if (_loaded &&
            _projectedLeagueId == leagueId &&
            _scoringSnapshot.ScoringType == scoring &&
            _scoringSnapshot.Week == week)
        {
            return;
        }

        lock (_gate)
        {
            if (_loaded &&
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
        var watch = Stopwatch.StartNew();
        try
        {
            var league = _leagueState.CurrentLeague;
            var context = ProjectionLeagueContext.FromLeague(league);
            var players = _players.GetAllPlayers();
            var profiles = _intelligence.GetAllProfiles()
                .ToDictionary(p => p.PlayerId);

            var projections = _engine.ProjectMany(players, profiles, context);
            watch.Stop();

            _projections = projections;
            _byPlayer = projections.ToDictionary(p => p.PlayerId);
            _projectedLeagueId = context.LeagueId;
            _scoringSnapshot = new ScoringTypeSnapshot(context.ScoringType, context.CurrentWeek);

            var avgConfidence = projections.Count == 0
                ? 0
                : projections.Average(p => p.Confidence);

            _status.RecordSuccess(projections.Count, avgConfidence, watch.Elapsed);

            _logger.LogInformation(
                "Projection pipeline: {Players} players projected in {Ms} ms (avg confidence {Avg:0.0})",
                projections.Count,
                watch.ElapsedMilliseconds,
                avgConfidence);
        }
        catch (Exception ex)
        {
            watch.Stop();
            _status.RecordFailure(ex.Message);
            _logger.LogWarning(ex, "Projection pipeline failed");
            throw;
        }
    }

    private readonly record struct ScoringTypeSnapshot(Core.Leagues.ScoringType ScoringType, int Week);
}
