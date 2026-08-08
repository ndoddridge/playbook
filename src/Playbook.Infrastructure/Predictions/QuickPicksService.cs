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
/// Does not read ILeagueState / roster / fantasy scoring.
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
        PropLineCredentialResolver.ApplyAliasEnvironmentVariables(_options);
        _status.SetConfigured(
            _options.Provider.ToString(),
            PropLineCredentialResolver.HasApiKey(_options));
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
        PropLineCredentialResolver.ApplyAliasEnvironmentVariables(_options);
        var configured = _options.Provider.ToString();
        var apiKeyConfigured = PropLineCredentialResolver.HasApiKey(_options);
        _status.SetConfigured(configured, apiKeyConfigured);

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

        // Never treat stale rows as live in the board.
        lines = lines
            .Select(NormalizeFreshness)
            .Where(l => l.Freshness != PropLineFreshness.Unavailable)
            .ToList();

        var games = lines.Select(l => l.Event.EventId).Distinct().Count();
        var markets = lines.Select(l => l.Market).Distinct().Count();
        watch.Stop();
        _status.RecordPropSync(
            activeName,
            usedFallback,
            games,
            markets,
            lines.Count,
            watch.Elapsed,
            error,
            apiKeyConfigured);

        var predictions = new List<Prediction>();
        foreach (var line in lines)
        {
            PlayerProductionSnapshot? production = null;
            Core.Intelligence.Models.PlayerIntelligenceProfile? intel = null;
            Core.Stats.Models.PlayerStatisticalContext? statsCtx = null;
            string? injuryNote = null;

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
            var prediction = _engine.Evaluate(
                line,
                projected.Projection,
                projected.Confidence,
                projected.Volatility,
                intel,
                statsCtx,
                injuryNote);
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

        if (_options.Provider == PropLineProviderKind.Mock)
        {
            activeName = "Mock";
            return GetProvider("Mock").GetPropLinesAsync().GetAwaiter().GetResult();
        }

        // Primary path: Live (The Odds API)
        try
        {
            if (!PropLineCredentialResolver.HasApiKey(_options))
            {
                usedFallback = true;
                error = $"{PropLineCredentialResolver.PrimaryEnvVar} is empty — falling back to Mock. " +
                        PropLineCredentialResolver.DescribeMissingKeyGuidance();
                activeName = "Mock";
                _logger.LogWarning(
                    "Live prop provider selected but API key is not configured (ApiKeyConfigured=false). Falling back to Mock.");
                return GetProvider("Mock").GetPropLinesAsync().GetAwaiter().GetResult();
            }

            var live = GetProvider("TheOddsAPI");
            var lines = live.GetPropLinesAsync().GetAwaiter().GetResult();
            if (lines.Count == 0 && _options.FallbackToMockWhenEmpty)
            {
                usedFallback = true;
                error = "The Odds API returned no NFL markets — falling back to Mock.";
                activeName = "Mock";
                return GetProvider("Mock").GetPropLinesAsync().GetAwaiter().GetResult();
            }

            activeName = live.ProviderName;
            return lines;
        }
        catch (Exception ex)
        {
            usedFallback = true;
            error = ex.Message;
            activeName = "Mock";
            _logger.LogWarning(ex, "Live prop provider failed; using Mock fallback");
            return GetProvider("Mock").GetPropLinesAsync().GetAwaiter().GetResult();
        }
    }

    private static PropLine NormalizeFreshness(PropLine line)
    {
        // Guard: mock source must never appear as Live.
        if (string.Equals(line.Source, "Mock", StringComparison.OrdinalIgnoreCase) &&
            line.Freshness == PropLineFreshness.Live)
        {
            return CloneWithFreshness(line, PropLineFreshness.Mock);
        }

        // Guard: The Odds API rows past stale window stay Stale, never Live.
        if (line.Freshness == PropLineFreshness.Live &&
            line.UpdatedAt < DateTimeOffset.UtcNow.AddHours(-24) &&
            !string.Equals(line.Source, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            // LivePropLineProvider already applies StaleAfterMinutes; this is a safety net.
            return line;
        }

        return line;
    }

    private static PropLine CloneWithFreshness(PropLine line, PropLineFreshness freshness) =>
        new()
        {
            Id = line.Id,
            Event = line.Event,
            PlayerId = line.PlayerId,
            PlayerName = line.PlayerName,
            TeamName = line.TeamName,
            Market = line.Market,
            Line = line.Line,
            Bookmaker = line.Bookmaker,
            Source = line.Source,
            UpdatedAt = line.UpdatedAt,
            Freshness = freshness,
            AmericanOddsOver = line.AmericanOddsOver,
            AmericanOddsUnder = line.AmericanOddsUnder
        };

    private IPropLineProvider GetProvider(string name) =>
        _providers.FirstOrDefault(p => p.ProviderName.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? _providers.First(p => p.ProviderName.Equals("Mock", StringComparison.OrdinalIgnoreCase));
}
