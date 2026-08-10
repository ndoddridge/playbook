using Playbook.Application.Intelligence;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.News;
using Playbook.Application.Players.Data;
using Playbook.Core.Intelligence.Models;
using Playbook.Infrastructure.Intelligence.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class IntelligenceServiceTests
{
    [Fact]
    public void Analyzer_Is_Deterministic_For_Same_Inputs()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Mock);
        var news = provider.GetRequiredService<INewsProvider>();
        var players = provider.GetRequiredService<Playbook.Application.Players.IPlayerService>();
        var analyzer = provider.GetRequiredService<IIntelligenceAnalyzer>();

        var articles = news.GetLatest(20);
        var catalog = players.GetAllPlayers();
        var first = analyzer.Analyze(articles, catalog);
        var second = analyzer.Analyze(articles, catalog);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.Select(f => f.Id), second.Select(f => f.Id));
        Assert.All(first, f => Assert.NotEmpty(f.RelatedNewsArticleIds));
        Assert.All(first, f => Assert.Contains(f.SupportingEvidence, e => e.StartsWith("Rule:")));
    }

    [Fact]
    public void Aggregator_Builds_Profiles_Deterministically()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Mock);
        var intelligence = provider.GetRequiredService<IIntelligenceService>();
        var status = provider.GetRequiredService<IIntelligenceSyncStatus>();

        var first = intelligence.GetAllProfiles();
        intelligence.Refresh();
        var second = intelligence.GetAllProfiles();

        Assert.NotEmpty(first);
        Assert.Equal(first.Select(p => p.PlayerId), second.Select(p => p.PlayerId));
        Assert.Equal(first.Select(p => p.OverallConfidence), second.Select(p => p.OverallConfidence));
        Assert.True(status.ProfilesGenerated > 0);
        Assert.True(status.FactsAggregated > 0);
        Assert.True(status.AverageFactsPerPlayer > 0);
        Assert.NotNull(status.AggregationRuntime);
    }

    [Fact]
    public void IntelligenceService_Generates_Facts_And_Profiles_From_News()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Mock);
        var intelligence = provider.GetRequiredService<IIntelligenceService>();
        var status = provider.GetRequiredService<IIntelligenceSyncStatus>();

        var top = intelligence.GetTopProfiles(5);

        Assert.NotEmpty(top);
        Assert.True(status.ArticlesProcessed > 0);
        Assert.True(status.FactsGenerated > 0);
        Assert.All(top, p => Assert.NotEmpty(p.SupportingFacts));
        Assert.All(top, p => Assert.False(string.IsNullOrWhiteSpace(p.Headline)));
    }

    [Fact]
    public void Injury_Heuristic_Produces_High_Importance()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Mock);
        var intelligence = provider.GetRequiredService<IIntelligenceService>();
        var facts = intelligence.GetAllFacts();

        Assert.Contains(facts, f =>
            f.Category == IntelligenceCategory.Injury &&
            f.Importance >= IntelligenceImportance.High);
    }

    [Fact]
    public void MockIntelligenceService_Still_Serves_Static_Catalog()
    {
        var sut = new MockIntelligenceService();
        var facts = sut.GetAllFacts();
        Assert.InRange(facts.Count, 75, 120);
        sut.Refresh();
        Assert.Equal(5, sut.GetTopFacts(5).Count);
        Assert.NotNull(sut.GetPlayerProfile(Guid.Parse("11111111-1111-1111-1111-111111111101")));
    }
}
