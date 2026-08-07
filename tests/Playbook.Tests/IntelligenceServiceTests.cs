using Playbook.Core.Intelligence.Models;
using Playbook.Infrastructure.Intelligence.Services;

namespace Playbook.Tests;

public class IntelligenceServiceTests
{
    private readonly MockIntelligenceService _sut = new();

    [Fact]
    public void GetAllFacts_ReturnsApproximatelySeventyFiveFacts()
    {
        var facts = _sut.GetAllFacts();

        Assert.InRange(facts.Count, 75, 120);
        Assert.All(facts, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Title));
            Assert.InRange(f.Confidence, 0, 100);
        });
    }

    [Fact]
    public void GetTopFacts_OrdersByImportanceThenConfidence()
    {
        var top = _sut.GetTopFacts(5);

        Assert.Equal(5, top.Count);
        for (var i = 1; i < top.Count; i++)
        {
            var prev = top[i - 1];
            var curr = top[i];
            Assert.True(
                prev.Importance > curr.Importance
                || (prev.Importance == curr.Importance && prev.Confidence >= curr.Confidence));
        }
    }

    [Fact]
    public void GetPlayerIntelligence_ReturnsFactsWithoutFantasyFields()
    {
        var playerId = Guid.Parse("11111111-1111-1111-1111-111111111101");
        var intel = _sut.GetPlayerIntelligence(playerId);

        Assert.NotNull(intel);
        Assert.Equal(playerId, intel!.PlayerId);
        Assert.NotEmpty(intel.Facts);
        Assert.False(string.IsNullOrWhiteSpace(intel.TrendSummary));
        Assert.False(string.IsNullOrWhiteSpace(intel.RiskSummary));
        Assert.False(string.IsNullOrWhiteSpace(intel.OpportunitySummary));

        // Intelligence stays football-only: facts never carry fantasy scoring language in category set
        Assert.All(intel.Facts, f => Assert.IsType<IntelligenceCategory>(f.Category));
    }
}
