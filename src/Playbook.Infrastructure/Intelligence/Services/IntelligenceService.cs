using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.News;
using Playbook.Application.Players;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;
using Playbook.Infrastructure.Injuries;

namespace Playbook.Infrastructure.Intelligence.Services;

/// <summary>
/// Intelligence Engine facade: analyze news → aggregate evidence → player profiles.
/// </summary>
public sealed class IntelligenceService : IIntelligenceService
{
    private readonly INewsProvider _news;
    private readonly IPlayerService _players;
    private readonly IPlayerInjuryService _injuries;
    private readonly IIntelligenceAnalyzer _analyzer;
    private readonly IIntelligenceAggregator _aggregator;
    private readonly IntelligenceSyncStatus _status;
    private readonly ILogger<IntelligenceService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<IntelligenceFact> _facts = [];
    private IReadOnlyList<PlayerIntelligenceProfile> _profiles = [];
    private bool _loaded;

    public IntelligenceService(
        INewsProvider news,
        IPlayerService players,
        IPlayerInjuryService injuries,
        IIntelligenceAnalyzer analyzer,
        IIntelligenceAggregator aggregator,
        IntelligenceSyncStatus status,
        ILogger<IntelligenceService> logger)
    {
        _news = news;
        _players = players;
        _injuries = injuries;
        _analyzer = analyzer;
        _aggregator = aggregator;
        _status = status;
        _logger = logger;
    }

    public IReadOnlyList<IntelligenceFact> GetAllFacts()
    {
        EnsureLoaded();
        return _facts;
    }

    public IReadOnlyList<IntelligenceFact> GetTopFacts(int count = 8)
    {
        EnsureLoaded();
        return _facts
            .OrderByDescending(f => f.Importance)
            .ThenByDescending(f => f.Confidence)
            .ThenBy(f => f.Id)
            .Take(Math.Max(1, count))
            .ToList();
    }

    public IReadOnlyList<IntelligenceFact> GetFactsForPlayer(Guid playerId)
    {
        EnsureLoaded();
        return _facts
            .Where(f => f.RelatedPlayerId == playerId)
            .OrderByDescending(f => f.Importance)
            .ThenByDescending(f => f.Confidence)
            .ToList();
    }

    public PlayerIntelligenceProfile? GetPlayerProfile(Guid playerId)
    {
        EnsureLoaded();
        return _profiles.FirstOrDefault(p => p.PlayerId == playerId);
    }

    public IReadOnlyList<PlayerIntelligenceProfile> GetTopProfiles(int count = 8)
    {
        EnsureLoaded();
        return _profiles
            .Where(p => p.ChangeSignal != IntelligenceChangeSignal.Neutral || p.SupportingFacts.Count > 0)
            .OrderByDescending(ScoreChangeMagnitude)
            .ThenByDescending(p => p.OverallConfidence)
            .ThenBy(p => p.PlayerId)
            .Take(Math.Max(1, count))
            .ToList();
    }

    public IReadOnlyList<PlayerIntelligenceProfile> GetAllProfiles()
    {
        EnsureLoaded();
        return _profiles;
    }

    public PlayerIntelligence? GetPlayerIntelligence(Guid playerId)
    {
        var profile = GetPlayerProfile(playerId);
        if (profile is null)
        {
            return null;
        }

        return new PlayerIntelligence
        {
            PlayerId = profile.PlayerId,
            OverallConfidence = profile.OverallConfidence,
            Facts = profile.SupportingFacts,
            TrendSummary = profile.Headline,
            RiskSummary = $"Risk {profile.OverallRisk} · Health {profile.HealthScore}",
            OpportunitySummary = $"Opportunity {profile.OpportunityScore} · Usage {profile.UsageScore}",
            LastUpdated = profile.LastUpdated,
            TrendDirection = profile.TrendDirection
        };
    }

    public void Refresh()
    {
        lock (_gate)
        {
            AnalyzeAndAggregateLocked();
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

            AnalyzeAndAggregateLocked();
            _loaded = true;
        }
    }

    private void AnalyzeAndAggregateLocked()
    {
        var analysisWatch = Stopwatch.StartNew();
        try
        {
            var articles = _news.GetLatest(50);
            var players = _players.GetAllPlayers();
            var newsFacts = _analyzer.Analyze(articles, players);
            var injuryProfiles = players
                .Select(p => _injuries.GetPlayerInjuryProfile(p.Id))
                .ToList();
            var injuryFacts = InjuryFactBuilder.BuildFacts(injuryProfiles);
            var facts = newsFacts.Concat(injuryFacts).ToList();
            analysisWatch.Stop();
            _facts = facts;
            _status.RecordAnalysisSuccess(articles.Count, facts.Count, analysisWatch.Elapsed);

            var aggregationWatch = Stopwatch.StartNew();
            var profiles = _aggregator.Aggregate(facts);
            aggregationWatch.Stop();
            _profiles = profiles;

            var factsAggregated = profiles.Sum(p => p.SupportingFacts.Count);
            _status.RecordAggregationSuccess(profiles.Count, factsAggregated, aggregationWatch.Elapsed);

            _logger.LogInformation(
                "Intelligence pipeline: {Articles} articles → {Facts} facts ({InjuryFacts} injury) ({AnalysisMs} ms) → {Profiles} profiles ({AggMs} ms)",
                articles.Count,
                facts.Count,
                injuryFacts.Count,
                analysisWatch.ElapsedMilliseconds,
                profiles.Count,
                aggregationWatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            analysisWatch.Stop();
            _status.RecordFailure(ex.Message);
            _logger.LogWarning(ex, "Intelligence pipeline failed");
            throw;
        }
    }

    private static int ScoreChangeMagnitude(PlayerIntelligenceProfile profile)
    {
        var healthSwing = Math.Abs(profile.HealthScore - 50);
        var opportunitySwing = Math.Abs(profile.OpportunityScore - 50);
        var usageSwing = Math.Abs(profile.UsageScore - 50);
        var signalBoost = profile.ChangeSignal switch
        {
            IntelligenceChangeSignal.HealthConcern => 40,
            IntelligenceChangeSignal.OpportunityIncreasing => 35,
            IntelligenceChangeSignal.OpportunityDecreasing => 30,
            IntelligenceChangeSignal.UsageIncreasing => 25,
            IntelligenceChangeSignal.ElevatedRisk => 28,
            IntelligenceChangeSignal.HealthImproving => 20,
            _ => 0
        };
        return healthSwing + opportunitySwing + usageSwing + profile.NewsMomentum + signalBoost;
    }
}
