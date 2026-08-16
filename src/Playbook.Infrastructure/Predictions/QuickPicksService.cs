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
using Playbook.Application.Research;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Knowledge;
using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;
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
    private readonly Application.Research.IPredictionResearchStore _research;
    private readonly ISharedEvidenceService _evidence;
    private readonly ITeamGameProjectionService _teamProjection;
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
        Application.Research.IPredictionResearchStore research,
        ISharedEvidenceService evidence,
        ITeamGameProjectionService teamProjection,
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
        _research = research;
        _evidence = evidence;
        _teamProjection = teamProjection;
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

    public (int GameMarketLines, int PlayerPropLines) GetSlateMarketCounts()
    {
        EnsureLoaded();

        var slateLines = _enrichedLines
            .Where(l => _selectedWeek is null || _selectedWeek.Matches(l.Event))
            .ToList();

        var gameLines = slateLines.Count(l => l.Market
            is PredictionMarketType.GameTotal
            or PredictionMarketType.TeamTotal
            or PredictionMarketType.Winner
            or PredictionMarketType.Spread);

        return (gameLines, slateLines.Count - gameLines);
    }

    public IReadOnlyList<GameMarketAnalysis> GetGameMarketAnalyses()
    {
        EnsureLoaded();

        var slateLines = _enrichedLines
            .Where(l => _selectedWeek is null || _selectedWeek.Matches(l.Event))
            .ToList();

        return GameMarketAnalysisBuilder.Build(
            slateLines,
            _teamProjection,
            TeamPointsModel.GameMarketBettingEnabled);
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
            error = ex.Message;
            activeName = configured;
            if (_options.AllowMockFallback)
            {
                _logger.LogWarning(ex, "Prop line load failed; using mock fallback");
                lines = GetProvider("Mock").GetPropLinesAsync().GetAwaiter().GetResult();
                usedFallback = true;
                activeName = "Mock";
            }
            else
            {
                _logger.LogWarning(ex, "Prop line load failed; mock fallback disabled, reporting unavailable");
                lines = [];
            }
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
        RecordBookmakerCoverage(_enrichedLines);

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
            Core.Players.PlayerStatus? rosterStatus = null;

            // Determine whether this is a game market or player prop.
            var isGameMarket = line.Market is PredictionMarketType.GameTotal
                or PredictionMarketType.TeamTotal
                or PredictionMarketType.Winner
                or PredictionMarketType.Spread;

            decimal? projection = null;
            int projConfidence = 0;
            int volatility = 100;
            bool usingPrior = false;

            if (isGameMarket)
            {
                // Game market: use team-level aggregation.
                var gameMarketProj = GameMarketProjector.ProjectGameMarket(
                    line.Market, line.Event, line, _teamProjection, line.Event.Phase);
                projection = gameMarketProj.Projection;
                projConfidence = gameMarketProj.Confidence;
                volatility = gameMarketProj.Volatility;
                // Game markets don't use prior production; set usingPrior = false.
            }
            else
            {
                // Player prop: use player-level projection.
                if (line.PlayerId is Guid playerId)
                {
                    var player = _players.GetPlayer(playerId);
                    if (player is not null)
                    {
                        production = _production.GetProduction(player);
                        rosterStatus = player.Status;
                    }

                    intel = _intelligence.GetPlayerProfile(playerId);
                    statsCtx = _stats.GetContext(playerId);
                    injuryProfile = _injuries.GetPlayerInjuryProfile(playerId);
                    facts = _intelligence.GetFactsForPlayer(playerId);
                }

                var projected = PropStatProjector.Project(
                    line.Market, production, statsCtx, intel, line.Event.Phase);
                projection = projected.Projection;
                projConfidence = projected.Confidence;
                volatility = projected.Volatility;
                usingPrior = projected.UsingPriorRegularSeasonProduction;
            }

            // Injury adjustments only apply to player props (not game markets).
            if (!isGameMarket)
            {
                var injuryMult = InjuryIntelligenceMapping.ProjectionHealthMultiplier(injuryProfile?.CurrentInjury);
                if (projection is not null && injuryMult is not null)
                {
                    projection = Math.Round(projection.Value * injuryMult.Value, 1, MidpointRounding.AwayFromZero);
                    projConfidence = Math.Clamp(projConfidence - (injuryMult < 0.5m ? 14 : 6), 10, 95);
                    volatility = Math.Clamp(volatility + (injuryMult < 0.5m ? 12 : 6), 10, 95);
                }
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
                RosterStatus = rosterStatus,
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
                RosterStatus = evaluation.RosterStatus,
                RecentFacts = evaluation.RecentFacts,
                SeasonPhase = evaluation.SeasonPhase,
                UsingPriorRegularSeasonProduction = evaluation.UsingPriorRegularSeasonProduction,
                Production = evaluation.Production,
                PredictionContext = predictionContext
            };

            var prediction = _engine.Evaluate(evaluation);

            // Game-market bets are withheld. The team-points model predicts points well (it beats
            // every naive baseline on two held-out seasons) but it does NOT beat a closing line:
            // backtested against real closing lines it wins 49.8% of totals and 48.9% of spreads
            // against a 52.4% break-even, and on totals it gets WORSE as its disagreement widens.
            // See TeamPointsModel.GameMarketBettingEnabled for the full table.
            //
            // The projection itself is still produced and remains visible; only the wager is
            // suppressed. Loosening this to populate the board would ship a losing strategy.
            if (prediction is not null && isGameMarket && !TeamPointsModel.GameMarketBettingEnabled)
            {
                prediction = null;
            }
            else if (prediction is not null
                && isGameMarket
                && Math.Abs(prediction.Edge) < TeamPointsModel.MinimumGameMarketEdgePoints)
            {
                prediction = null;
            }

            if (prediction is not null)
            {
                // Knowledge Impact: bounded ranking adjustment (Baseline = no-op).
                prediction = _knowledgeImpact.ApplyToQuickPickPrediction(
                    prediction,
                    evaluation.PredictionContext);

                // Shared research-memory evidence: informational only — see QuickPickEvidenceEnricher.
                // Only Confidence/Edge/Probability/OpportunityScore already set above ever drive the
                // pick's ranking; this can only add supporting-intelligence text.
                if (prediction.PlayerId is Guid evidencePlayerId)
                {
                    prediction = QuickPickEvidenceEnricher.Apply(
                        prediction, _evidence.GetEvidenceForPlayer(evidencePlayerId));
                }

                predictions.Add(prediction);
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

        CaptureResearchSnapshots(_predictions);
    }

    /// <summary>
    /// Permanent pre-event research memory: one immutable snapshot per prediction built on a real
    /// (non-mock, non-stale) sportsbook line. Never blocks or alters the Quick Picks board itself —
    /// a persistence failure here is logged and swallowed, matching the isolation of every other
    /// background-refresh stage.
    /// </summary>
    private void CaptureResearchSnapshots(IReadOnlyList<Prediction> predictions)
    {
        foreach (var prediction in predictions)
        {
            if (prediction.LineFreshness != PropLineFreshness.Live)
            {
                continue;
            }

            try
            {
                _research.SaveSnapshot(new Core.Research.PredictionSnapshot
                {
                    SnapshotId = prediction.Id,
                    PlayerId = prediction.PlayerId,
                    PlayerName = prediction.PlayerName,
                    EventId = prediction.Event.EventId,
                    CommenceTime = prediction.Event.CommenceTime,
                    Season = prediction.Event.Season,
                    Week = prediction.Event.Week,
                    SeasonPhase = prediction.Event.Phase,
                    Market = prediction.Market,
                    Direction = prediction.Direction,
                    Line = prediction.Line,
                    Bookmaker = prediction.Bookmaker ?? "unknown",
                    LineSource = prediction.Source,
                    LineUpdatedAt = prediction.LineUpdatedAt ?? prediction.LastUpdated,
                    PlaybookProjection = prediction.PlaybookProjection,
                    Probability = prediction.Probability,
                    Confidence = prediction.Confidence,
                    Reasoning = prediction.Reasoning,
                    SupportingIntelligence = prediction.SupportingIntelligence,
                    CapturedAt = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture research snapshot for prediction {Id}", prediction.Id);
            }
        }
    }

    private IReadOnlyList<PropLine> LoadLines(out bool usedFallback, out string activeName, out string? error)
    {
        usedFallback = false;
        error = null;

        if (_options.Provider == PropLineProviderKind.Mock)
        {
            // Explicit dev choice, not a fallback — always honored regardless of AllowMockFallback.
            activeName = "Mock";
            return GetProvider("Mock").GetPropLinesAsync().GetAwaiter().GetResult();
        }

        if (!PropLineCredentialResolver.HasApiKey(_options))
        {
            error = $"{PropLineCredentialResolver.PrimaryEnvVar} is empty. " +
                    PropLineCredentialResolver.DescribeMissingKeyGuidance();
            activeName = "TheOddsAPI";
            _logger.LogWarning(
                "Live prop provider selected but API key is not configured (ApiKeyConfigured=false).");
            if (!_options.AllowMockFallback)
            {
                return [];
            }

            usedFallback = true;
            activeName = "Mock";
            error += " Falling back to Mock.";
            return GetProvider("Mock").GetPropLinesAsync().GetAwaiter().GetResult();
        }

        try
        {
            var live = GetProvider("TheOddsAPI");
            var lines = live.GetPropLinesAsync().GetAwaiter().GetResult();
            if (lines.Count == 0)
            {
                error = "The Odds API returned no NFL markets right now.";
                activeName = live.ProviderName;
                if (_options.FallbackToMockWhenEmpty && _options.AllowMockFallback)
                {
                    usedFallback = true;
                    activeName = "Mock";
                    error += " Falling back to Mock.";
                    return GetProvider("Mock").GetPropLinesAsync().GetAwaiter().GetResult();
                }

                return [];
            }

            activeName = live.ProviderName;
            return lines;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            activeName = "TheOddsAPI";
            _logger.LogWarning(ex, "Live prop provider failed.");
            if (!_options.AllowMockFallback)
            {
                return [];
            }

            usedFallback = true;
            activeName = "Mock";
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

    private static readonly IReadOnlyDictionary<string, string> BookmakerFriendlyNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["williamhill_us"] = "Caesars Sportsbook",
            ["caesars"] = "Caesars Sportsbook",
            ["draftkings"] = "DraftKings",
            ["fanduel"] = "FanDuel",
            ["betmgm"] = "BetMGM"
        };

    /// <summary>
    /// Honest transparency, not a fix to the priority logic itself (that's already correct and
    /// tested): records whether the configured primary bookmaker actually supplied a line this
    /// sync, so "DraftKings shown instead of Caesars" is visibly explained by data-provider
    /// coverage rather than looking like a silent bug.
    /// </summary>
    private void RecordBookmakerCoverage(IReadOnlyList<PropLine> lines)
    {
        var priority = LivePropLineProvider.ParseBookmakerPriority(_options.OddsApi.PreferredBookmakers);
        var (primaryName, primaryActive, summary) = ComputeBookmakerCoverage(lines, priority, BookmakerFriendlyNames);
        if (primaryName is null)
        {
            return;
        }

        _status.RecordBookmakerCoverage(primaryName, primaryActive, summary);
    }

    /// <summary>Pure computation, directly unit-testable without constructing a full service.</summary>
    internal static (string? PrimaryName, bool PrimaryActive, string Summary) ComputeBookmakerCoverage(
        IReadOnlyList<PropLine> lines,
        IReadOnlyList<string> priority,
        IReadOnlyDictionary<string, string> friendlyNames)
    {
        if (priority.Count == 0)
        {
            return (null, false, "none");
        }

        var primaryKey = priority[0];
        var primaryName = friendlyNames.GetValueOrDefault(primaryKey, primaryKey);

        var counts = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Bookmaker) &&
                        string.Equals(l.Source, "TheOddsAPI", StringComparison.OrdinalIgnoreCase))
            .GroupBy(l => l.Bookmaker!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        var primaryActive = counts.Any(g =>
            NormalizeForMatch(g.Key) == NormalizeForMatch(primaryName) ||
            NormalizeForMatch(g.Key) == NormalizeForMatch(primaryKey));

        var summary = counts.Count == 0
            ? "none"
            : string.Join(", ", counts.Take(4).Select(g => $"{g.Key} ({g.Count()})"));

        return (primaryName, primaryActive, summary);
    }

    private static string NormalizeForMatch(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

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
