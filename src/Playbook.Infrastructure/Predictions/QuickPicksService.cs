using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Players;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Loads prop lines, builds football (non-fantasy) projections, and runs QuickPicksEngine.
/// Does not read ILeagueState.
/// </summary>
public sealed class QuickPicksService : IQuickPicksService
{
    private readonly IEnumerable<IPropLineProvider> _providers;
    private readonly IQuickPicksEngine _engine;
    private readonly IPlayerService _players;
    private readonly IPlayerProductionProvider _production;
    private readonly IIntelligenceService _intelligence;
    private readonly IPlayerStatisticalContextService _stats;
    private readonly IPlayerInjuryService _injuries;
    private readonly PropLineOptions _options;
    private readonly QuickPicksSyncStatus _status;
    private readonly ILogger<QuickPicksService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<Prediction> _predictions = [];
    private IReadOnlyList<FootballEvent> _events = [];
    private bool _loaded;

    public QuickPicksService(
        IEnumerable<IPropLineProvider> providers,
        IQuickPicksEngine engine,
        IPlayerService players,
        IPlayerProductionProvider production,
        IIntelligenceService intelligence,
        IPlayerStatisticalContextService stats,
        IPlayerInjuryService injuries,
        IOptions<PropLineOptions> options,
        QuickPicksSyncStatus status,
        ILogger<QuickPicksService> logger)
    {
        _providers = providers;
        _engine = engine;
        _players = players;
        _production = production;
        _intelligence = intelligence;
        _stats = stats;
        _injuries = injuries;
        _options = options.Value;
        _status = status;
        _logger = logger;
        _status.SetConfigured(_options.Provider.ToString());
    }

    public IReadOnlyList<Prediction> GetAllPredictions()
    {
        EnsureLoaded();
        return _predictions;
    }

    public IReadOnlyList<Prediction> GetTopPicks(int count = 8)
    {
        EnsureLoaded();
        return _predictions
            .Where(p => p.LineFreshness is PropLineFreshness.Live or PropLineFreshness.Mock)
            .Where(p => p.Confidence >= 55 && Math.Abs(p.Edge) >= 0.4m && p.Probability >= 55)
            .OrderByDescending(p => p.Edge)
            .ThenByDescending(p => p.Probability)
            .ThenByDescending(p => p.Confidence)
            .Take(Math.Max(1, count))
            .ToList();
    }

    public IReadOnlyList<Prediction> GetWatchPicks(int count = 8)
    {
        EnsureLoaded();
        var topIds = GetTopPicks(count).Select(p => p.Id).ToHashSet();
        return _predictions
            .Where(p => !topIds.Contains(p.Id))
            .Where(p => p.LineFreshness != PropLineFreshness.Unavailable)
            .OrderByDescending(p => p.Probability)
            .ThenByDescending(p => p.Confidence)
            .Take(Math.Max(1, count))
            .ToList();
    }

    public IReadOnlyList<FootballEvent> GetUpcomingEvents()
    {
        EnsureLoaded();
        return _events;
    }

    public void Refresh()
    {
        lock (_gate)
        {
            BuildLocked();
            _loaded = true;
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            BuildLocked();
            _loaded = true;
        }
    }

    private void BuildLocked()
    {
        var watch = Stopwatch.StartNew();
        var configured = _options.Provider.ToString();
        _status.SetConfigured(configured);

        IReadOnlyList<PropLine> lines;
        var usedFallback = false;
        string? error = null;
        var activeName = configured;

        try
        {
            lines = LoadLines(out usedFallback, out activeName, out error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prop line load failed; using mock fallback");
            lines = GetProvider("Mock").GetPropLinesAsync().GetAwaiter().GetResult();
            usedFallback = true;
            activeName = "Mock";
            error = ex.Message;
        }

        var games = lines.Select(l => l.Event.EventId).Distinct().Count();
        var markets = lines.Select(l => l.Market).Distinct().Count();
        _status.RecordPropSync(activeName, usedFallback, games, markets, lines.Count, watch.Elapsed, error);

        var predictions = new List<Prediction>();
        foreach (var line in lines)
        {
            PlayerProductionSnapshot? production = null;
            Core.Intelligence.Models.PlayerIntelligenceProfile? intel = null;
            Core.Stats.Models.PlayerStatisticalContext? statsCtx = null;
            string? injuryNote = null;
            decimal? projection = null;
            var confidence = 40;
            var volatility = 55;

            if (line.PlayerId is Guid playerId)
            {
                var player = _players.GetPlayer(playerId);
                if (player is not null)
                {
                    production = _production.GetProduction(player);
                }

                intel = _intelligence.GetPlayerProfile(playerId);
                statsCtx = _stats.GetContext(playerId);
                var injury = _injuries.GetCurrentInjury(playerId);
                if (injury is not null &&
                    !string.Equals(injury.Status, "Active", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(injury.Status, "Healthy", StringComparison.OrdinalIgnoreCase))
                {
                    injuryNote = $"{injury.Status}" +
                                 (string.IsNullOrWhiteSpace(injury.BodyPart) ? "" : $" ({injury.BodyPart})");
                }
            }

            var projected = PropStatProjector.Project(line.Market, production, statsCtx, intel);
            projection = projected.Projection;
            confidence = projected.Confidence;
            volatility = projected.Volatility;

            var prediction = _engine.Evaluate(
                line, projection, confidence, volatility, intel, statsCtx, injuryNote);
            if (prediction is not null)
            {
                predictions.Add(prediction);
            }
        }

        _predictions = predictions
            .OrderByDescending(p => p.Edge)
            .ThenByDescending(p => p.Probability)
            .ToList();
        _events = lines
            .Select(l => l.Event)
            .GroupBy(e => e.EventId)
            .Select(g => g.First())
            .OrderBy(e => e.CommenceTime)
            .ToList();

        var avgConf = _predictions.Count == 0 ? 0 : _predictions.Average(p => p.Confidence);
        _status.RecordPredictions(_predictions.Count, avgConf);
        watch.Stop();

        _logger.LogInformation(
            "Quick Picks: {Predictions} predictions from {Props} props ({Provider}{Fallback}) in {Ms} ms",
            _predictions.Count,
            lines.Count,
            activeName,
            usedFallback ? ", fallback" : "",
            watch.ElapsedMilliseconds);
    }

    private IReadOnlyList<PropLine> LoadLines(out bool usedFallback, out string activeName, out string? error)
    {
        usedFallback = false;
        error = null;
        activeName = _options.Provider.ToString();

        if (_options.Provider == PropLineProviderKind.Live)
        {
            try
            {
                var live = GetProvider("TheOddsAPI");
                activeName = live.ProviderName;
                return live.GetPropLinesAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                usedFallback = true;
                error = ex.Message;
                activeName = "Mock";
                return GetProvider("Mock").GetPropLinesAsync().GetAwaiter().GetResult();
            }
        }

        activeName = "Mock";
        return GetProvider("Mock").GetPropLinesAsync().GetAwaiter().GetResult();
    }

    private IPropLineProvider GetProvider(string name) =>
        _providers.FirstOrDefault(p => p.ProviderName.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? _providers.First(p => p.ProviderName.Equals("Mock", StringComparison.OrdinalIgnoreCase));
}
