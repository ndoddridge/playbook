using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Knowledge;
using Playbook.Application.Players;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Knowledge;
using Playbook.Core.Predictions;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Loads prop lines, tags NFL week/game context, filters to one week slate,
/// builds football (non-fantasy) projections, and runs QuickPicksEngine.
/// Does not read ILeagueState / roster / fantasy scoring.
/// </summary>
public sealed class QuickPicksService : IQuickPicksService
{
    private readonly IEnumerable<IPropLineProvider> _providers;
    private readonly IQuickPicksEngine _engine;
    private readonly INflCalendarService _calendar;
    private readonly IPlayerService _players;
    private readonly IPlayerProductionProvider _production;
    private readonly IIntelligenceService _intelligence;
    private readonly IPlayerStatisticalContextService _stats;
    private readonly IPlayerInjuryService _injuries;
    private readonly ISharedKnowledgeModel _sharedKnowledge;
    private readonly IKnowledgeImpactApplicator _knowledgeImpact;
    private readonly PropLineOptions _options;
    private readonly QuickPicksSyncStatus _status;
    private readonly ILogger<QuickPicksService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<Prediction> _predictions = [];
    private IReadOnlyList<FootballEvent> _events = [];
    private IReadOnlyList<NflWeekRef> _availableWeeks = [];
    private IReadOnlyList<NflWeekRef> _canonicalWeeks = [];
    private IReadOnlyList<NflSlate> _availableSlates = [];
    private NflWeekRef? _selectedWeek;
    private NflWeekRef? _preferredWeek;
    private NflSeasonContext? _seasonContext;
    private IReadOnlyList<PropLine> _enrichedLines = [];
    private bool _loaded;

    public QuickPicksService(
        IEnumerable<IPropLineProvider> providers,
        IQuickPicksEngine engine,
        INflCalendarService calendar,
        IPlayerService players,
        IPlayerProductionProvider production,
        IIntelligenceService intelligence,
        IPlayerStatisticalContextService stats,
        IPlayerInjuryService injuries,
        ISharedKnowledgeModel sharedKnowledge,
        IKnowledgeImpactApplicator knowledgeImpact,
        IOptions<PropLineOptions> options,
        QuickPicksSyncStatus status,
        ILogger<QuickPicksService> logger)
    {
        _providers = providers;
        _engine = engine;
        _calendar = calendar;
        _players = players;
        _production = production;
        _intelligence = intelligence;
        _stats = stats;
        _injuries = injuries;
        _sharedKnowledge = sharedKnowledge;
        _knowledgeImpact = knowledgeImpact;
        _options = options.Value;
        _status = status;
        _logger = logger;
        PropLineCredentialResolver.ApplyAliasEnvironmentVariables(_options);
        _status.SetConfigured(
            _options.Provider.ToString(),
            PropLineCredentialResolver.HasApiKey(_options));
    }

    public NflWeekRef? SelectedWeek
    {
        get
        {
            EnsureLoaded();
            return _selectedWeek;
        }
    }

    public IReadOnlyList<NflSlate> AvailableSlates
    {
        get
        {
            EnsureLoaded();
            return _availableSlates;
        }
    }

    public IReadOnlyList<NflWeekRef> AvailableWeeks
    {
        get
        {
            EnsureLoaded();
            return _availableWeeks;
        }
    }

    public IReadOnlyList<NflWeekRef> CanonicalWeeks
    {
        get
        {
            EnsureLoaded();
            return _canonicalWeeks;
        }
    }

    public NflSeasonContext? SeasonContext
    {
        get
        {
            EnsureLoaded();
            return _seasonContext;
        }
    }

    public IReadOnlyList<Prediction> GetAllPredictions()
    {
        EnsureLoaded();
        return _predictions;
    }

    public IReadOnlyList<Prediction> GetTopPicks(int count = 5)
    {
        EnsureLoaded();
        // Strongest distinct opportunities on the slate (diversity preference, no confidence floor).
        return QuickPickSearch.SelectDiverseTop(
            _predictions,
            count,
            p => _selectedWeek is null || _selectedWeek.Matches(p.Event));
    }

    public IReadOnlyList<Prediction> GetWatchPicks(int count = 8, int topCount = 5)
    {
        EnsureLoaded();
        var topIds = GetTopPicks(topCount).Select(p => p.Id).ToHashSet();
        return RankEligiblePredictions()
            .Where(p => !topIds.Contains(p.Id))
            .Take(Math.Max(1, count))
            .ToList();
    }

    private IEnumerable<Prediction> RankEligiblePredictions() =>
        QuickPickSearch.RankEligible(
            _predictions,
            p => _selectedWeek is null || _selectedWeek.Matches(p.Event));

    public IReadOnlyList<Prediction> GetSlatePredictions()
    {
        EnsureLoaded();
        return _predictions;
    }

    public IReadOnlyList<FootballEvent> GetUpcomingEvents()
    {
        EnsureLoaded();
        return _events;
    }

    public bool TrySelectWeek(NflWeekRef week)
    {
        ArgumentNullException.ThrowIfNull(week);
        lock (_gate)
        {
            EnsureLoadedLocked();
            if (!_canonicalWeeks.Contains(week))
            {
                return false;
            }

            _preferredWeek = week;
            BuildPredictionsForSelectedWeekLocked();
            return true;
        }
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
            EnsureLoadedLocked();
        }
    }

    private void EnsureLoadedLocked()
    {
        if (_loaded)
        {
            return;
        }

        BuildLocked();
        _loaded = true;
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

        lines = lines
            .Select(NormalizeFreshness)
            .Where(l => l.Freshness != PropLineFreshness.Unavailable)
            .ToList();

        var season = _calendar.GetCurrentContext();

        var rawEvents = lines
            .Select(l => l.Event)
            .GroupBy(e => e.EventId)
            .Select(g => g.First())
            .ToList();

        // If regular-season Odds events are present, record their week-start as the RS open
        // so phase inference stays event-driven (never invents weeks).
        var firstRegular = rawEvents
            .Where(e => e.PhaseHint == NflSeasonPhase.RegularSeason || e.Phase == NflSeasonPhase.RegularSeason)
            .OrderBy(e => e.CommenceTime)
            .FirstOrDefault();
        if (firstRegular is not null && season.RegularSeasonStartDate is null)
        {
            season = new NflSeasonContext
            {
                Season = season.Season,
                Phase = season.Phase,
                Week = season.Week,
                DisplayWeek = season.DisplayWeek,
                PhaseStartDate = season.PhaseStartDate,
                RegularSeasonStartDate = NflCalendarService.NflWeekStartEastern(firstRegular.CommenceTime),
                ResolvedAt = season.ResolvedAt,
                Source = season.Source
            };
        }

        _seasonContext = season;

        var enrichedEvents = _calendar.EnrichEvents(rawEvents, season)
            .ToDictionary(e => e.EventId, StringComparer.OrdinalIgnoreCase);

        _enrichedLines = lines
            .Select(l => CloneWithEvent(l, enrichedEvents.TryGetValue(l.Event.EventId, out var ev) ? ev : l.Event))
            .ToList();

        var distinctEvents = _enrichedLines
            .Select(l => l.Event)
            .GroupBy(e => e.EventId)
            .Select(g => g.First())
            .ToList();
        _availableSlates = _calendar.BuildSlates(distinctEvents);
        _availableWeeks = _availableSlates.Select(s => s.Ref).ToList();
        _canonicalWeeks = NflWeekRef.BuildCanonicalSeason(season.Season);
        _selectedWeek = _calendar.SelectActiveWeek(_availableSlates, season, _preferredWeek);

        var games = _enrichedLines.Select(l => l.Event.EventId).Distinct().Count();
        var markets = _enrichedLines.Select(l => l.Market).Distinct().Count();
        watch.Stop();
        _status.RecordPropSync(
            activeName,
            usedFallback,
            games,
            markets,
            _enrichedLines.Count,
            watch.Elapsed,
            error,
            apiKeyConfigured);

        BuildPredictionsForSelectedWeekLocked();

        _logger.LogInformation(
            "Quick Picks: {Predictions} predictions from {Props} props · slate {Slate} ({Provider}{Fallback})",
            _predictions.Count,
            _enrichedLines.Count,
            _selectedWeek?.DisplayLabel ?? "—",
            activeName,
            usedFallback ? ", fallback" : "");
    }

    private void BuildPredictionsForSelectedWeekLocked()
    {
        var season = _seasonContext ?? _calendar.GetCurrentContext();
        NflWeekRef selected;
        if (_preferredWeek is not null && _canonicalWeeks.Contains(_preferredWeek))
        {
            selected = _preferredWeek;
        }
        else
        {
            selected = _calendar.SelectActiveWeek(_availableSlates, season, _preferredWeek);
        }

        _selectedWeek = selected;

        // Never mix weeks: evaluate only lines for the active slate week.
        var weekLines = _enrichedLines
            .Where(l => selected.Matches(l.Event))
            .ToList();

        var predictions = new List<Prediction>();
        foreach (var line in weekLines)
        {
            PlayerProductionSnapshot? production = null;
            PlayerIntelligenceProfile? intel = null;
            Core.Stats.Models.PlayerStatisticalContext? statsCtx = null;
            PlayerInjuryProfile? injuryProfile = null;
            IReadOnlyList<IntelligenceFact> facts = [];

            if (line.PlayerId is Guid playerId)
            {
                var player = _players.GetPlayer(playerId);
                if (player is not null)
                {
                    production = _production.GetProduction(player);
                }

                intel = _intelligence.GetPlayerProfile(playerId);
                statsCtx = _stats.GetContext(playerId);
                injuryProfile = _injuries.GetPlayerInjuryProfile(playerId);
                facts = _intelligence.GetFactsForPlayer(playerId);
            }

            var projected = PropStatProjector.Project(
                line.Market, production, statsCtx, intel, line.Event.Phase);
            var projection = projected.Projection;
            var projConfidence = projected.Confidence;
            var volatility = projected.Volatility;
            var usingPrior = projected.UsingPriorRegularSeasonProduction;

            var injuryMult = InjuryIntelligenceMapping.ProjectionHealthMultiplier(injuryProfile?.CurrentInjury);
            if (projection is not null && injuryMult is not null)
            {
                projection = Math.Round(projection.Value * injuryMult.Value, 1, MidpointRounding.AwayFromZero);
                projConfidence = Math.Clamp(projConfidence - (injuryMult < 0.5m ? 14 : 6), 10, 95);
                volatility = Math.Clamp(volatility + (injuryMult < 0.5m ? 12 : 6), 10, 95);
            }

            var evaluation = new QuickPickEvaluationContext
            {
                Line = line,
                PlaybookProjection = projection,
                ProjectionConfidence = projConfidence,
                Volatility = volatility,
                Intelligence = intel,
                StatisticalContext = statsCtx,
                InjuryProfile = injuryProfile,
                RecentFacts = facts,
                SeasonPhase = line.Event.Phase,
                UsingPriorRegularSeasonProduction = usingPrior,
                Production = production
            };

            // Shared knowledge substrate — first-class consumer; scoring unchanged.
            var predictionContext = _sharedKnowledge.BuildQuickPickPredictionContext(evaluation);
            KnowledgeTemporalGuard.AssertNoFutureLeak(
                predictionContext.Knowledge,
                predictionContext.InformationCutoff);
            evaluation = new QuickPickEvaluationContext
            {
                Line = evaluation.Line,
                PlaybookProjection = evaluation.PlaybookProjection,
                ProjectionConfidence = evaluation.ProjectionConfidence,
                Volatility = evaluation.Volatility,
                Intelligence = evaluation.Intelligence,
                StatisticalContext = evaluation.StatisticalContext,
                InjuryProfile = evaluation.InjuryProfile,
                RecentFacts = evaluation.RecentFacts,
                SeasonPhase = evaluation.SeasonPhase,
                UsingPriorRegularSeasonProduction = evaluation.UsingPriorRegularSeasonProduction,
                Production = evaluation.Production,
                PredictionContext = predictionContext
            };

            var prediction = _engine.Evaluate(evaluation);
            if (prediction is not null)
            {
                // Knowledge Impact: bounded ranking adjustment (Baseline = no-op).
                predictions.Add(_knowledgeImpact.ApplyToQuickPickPrediction(
                    prediction,
                    evaluation.PredictionContext));
            }
        }

        _predictions = predictions
            .OrderByDescending(p => p.OpportunityScore)
            .ThenByDescending(p => p.Edge)
            .ThenByDescending(p => p.Probability)
            .ToList();

        _events = weekLines
            .Select(l => l.Event)
            .GroupBy(e => e.EventId)
            .Select(g => g.First())
            .OrderBy(e => e.CommenceTime)
            .ToList();

        var avgConf = _predictions.Count == 0 ? 0 : _predictions.Average(p => p.Confidence);
        _status.RecordPredictions(_predictions.Count, avgConf);
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
        if (string.Equals(line.Source, "Mock", StringComparison.OrdinalIgnoreCase) &&
            line.Freshness == PropLineFreshness.Live)
        {
            return CloneWithFreshness(line, PropLineFreshness.Mock);
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

    private static PropLine CloneWithEvent(PropLine line, FootballEvent ev) =>
        new()
        {
            Id = line.Id,
            Event = ev,
            PlayerId = line.PlayerId,
            PlayerName = line.PlayerName,
            TeamName = line.TeamName,
            Market = line.Market,
            Line = line.Line,
            Bookmaker = line.Bookmaker,
            Source = line.Source,
            UpdatedAt = line.UpdatedAt,
            Freshness = line.Freshness,
            AmericanOddsOver = line.AmericanOddsOver,
            AmericanOddsUnder = line.AmericanOddsUnder
        };

    private IPropLineProvider GetProvider(string name) =>
        _providers.FirstOrDefault(p => p.ProviderName.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? _providers.First(p => p.ProviderName.Equals("Mock", StringComparison.OrdinalIgnoreCase));
}
