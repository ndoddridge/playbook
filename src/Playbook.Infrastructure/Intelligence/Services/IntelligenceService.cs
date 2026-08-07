using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Playbook.Application.Intelligence;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.News;
using Playbook.Application.Players;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Intelligence.Services;

/// <summary>
/// Intelligence Engine V1 facade. Analyzes live/mock news into deterministic IntelligenceFacts.
/// </summary>
public sealed class IntelligenceService : IIntelligenceService
{
    private readonly INewsProvider _news;
    private readonly IPlayerService _players;
    private readonly IIntelligenceAnalyzer _analyzer;
    private readonly IntelligenceSyncStatus _status;
    private readonly ILogger<IntelligenceService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<IntelligenceFact> _facts = [];
    private bool _loaded;

    public IntelligenceService(
        INewsProvider news,
        IPlayerService players,
        IIntelligenceAnalyzer analyzer,
        IntelligenceSyncStatus status,
        ILogger<IntelligenceService> logger)
    {
        _news = news;
        _players = players;
        _analyzer = analyzer;
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

    public PlayerIntelligence? GetPlayerIntelligence(Guid playerId)
    {
        var facts = GetFactsForPlayer(playerId);
        if (facts.Count == 0)
        {
            return null;
        }

        var trend = InferTrend(facts);
        return new PlayerIntelligence
        {
            PlayerId = playerId,
            OverallConfidence = (int)Math.Round(facts.Average(f => f.Confidence)),
            Facts = facts,
            TrendSummary = SummarizeTrend(facts, trend),
            RiskSummary = SummarizeRisk(facts),
            OpportunitySummary = SummarizeOpportunity(facts),
            LastUpdated = facts.Max(f => f.Created),
            TrendDirection = trend
        };
    }

    public void Refresh()
    {
        lock (_gate)
        {
            AnalyzeLocked();
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

            AnalyzeLocked();
            _loaded = true;
        }
    }

    private void AnalyzeLocked()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var articles = _news.GetLatest(50);
            var players = _players.GetAllPlayers();
            var facts = _analyzer.Analyze(articles, players);
            stopwatch.Stop();

            _facts = facts;
            _status.RecordSuccess(articles.Count, facts.Count, stopwatch.Elapsed);
            _logger.LogInformation(
                "Intelligence analysis complete: {Articles} articles → {Facts} facts in {ElapsedMs} ms",
                articles.Count,
                facts.Count,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _status.RecordFailure(ex.Message);
            _logger.LogWarning(ex, "Intelligence analysis failed");
            throw;
        }
    }

    private static TrendDirection InferTrend(IReadOnlyList<IntelligenceFact> facts)
    {
        var up = facts.Count(f =>
            f.Category is IntelligenceCategory.Usage or IntelligenceCategory.Opportunity or IntelligenceCategory.Efficiency);
        var down = facts.Count(f =>
            f.Category is IntelligenceCategory.Injury or IntelligenceCategory.Suspension or IntelligenceCategory.Weather);
        if (up > down)
        {
            return TrendDirection.Up;
        }

        if (down > up)
        {
            return TrendDirection.Down;
        }

        return TrendDirection.Flat;
    }

    private static string SummarizeTrend(IReadOnlyList<IntelligenceFact> facts, TrendDirection trend)
    {
        var usage = facts.FirstOrDefault(f => f.Category is IntelligenceCategory.Usage or IntelligenceCategory.Opportunity);
        return trend switch
        {
            TrendDirection.Up => usage?.Title ?? "Positive football signals outweigh risks.",
            TrendDirection.Down => "Availability or role risk is the dominant recent signal.",
            _ => "Mixed or limited signals — monitoring for clearer direction."
        };
    }

    private static string SummarizeRisk(IReadOnlyList<IntelligenceFact> facts)
    {
        var risk = facts.FirstOrDefault(f =>
            f.Category is IntelligenceCategory.Injury or IntelligenceCategory.Suspension or IntelligenceCategory.Weather);
        return risk?.Description ?? "No elevated injury/suspension/weather risk detected from recent news.";
    }

    private static string SummarizeOpportunity(IReadOnlyList<IntelligenceFact> facts)
    {
        var opp = facts.FirstOrDefault(f =>
            f.Category is IntelligenceCategory.Opportunity or IntelligenceCategory.Usage or IntelligenceCategory.Transaction);
        return opp?.Description ?? "No clear opportunity spike detected from recent news.";
    }
}
