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
/// </summary>
public sealed class ProjectionService : IProjectionService
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
    private readonly object _gate = new();

    private IReadOnlyList<PlayerProjection> _projections = [];
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
        ILogger<ProjectionService> logger)
    {
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
    }

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

            _projections = projections;
            _byPlayer = projections.ToDictionary(p => p.PlayerId);
            _projectedLeagueId = context.LeagueId;
            _scoringSnapshot = new ScoringTypeSnapshot(context.ScoringType, context.CurrentWeek);

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
