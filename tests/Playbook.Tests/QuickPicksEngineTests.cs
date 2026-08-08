using Playbook.Application.Players.Data;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Infrastructure.Predictions;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class QuickPicksEngineTests
{
    private readonly QuickPicksEngine _engine = new();

    [Fact]
    public void Engine_Version_Is_0_1()
    {
        Assert.Equal("0.1", _engine.Version);
        Assert.Equal(QuickPicksEngine.CurrentVersion, _engine.Version);
    }

    [Fact]
    public void Projection_Above_Line_Favors_Over()
    {
        var prediction = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            playbookProjection: 108.2m,
            projectionConfidence: 80,
            volatility: 35,
            intelligence: Intel(usage: 70, health: 75),
            statisticalContext: null,
            injuryNote: null));

        Assert.Equal(PredictionDirection.Over, prediction.Direction);
        Assert.True(prediction.Edge > 0);
        Assert.True(prediction.Probability > 50);
        Assert.Contains("above", prediction.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Base projection", prediction.Reasoning, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_Below_Line_Favors_Under()
    {
        var prediction = Require(_engine.Evaluate(
            Line(PredictionMarketType.RushingYards, 90.5m),
            playbookProjection: 62.0m,
            projectionConfidence: 78,
            volatility: 30,
            intelligence: Intel(usage: 45, health: 70),
            statisticalContext: null,
            injuryNote: null));

        Assert.Equal(PredictionDirection.Under, prediction.Direction);
        Assert.True(prediction.Edge > 0);
        Assert.True(prediction.Probability > 50);
        Assert.Contains("below", prediction.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_Equal_To_Line_Has_Minimal_Edge()
    {
        var prediction = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            playbookProjection: 94.5m,
            projectionConfidence: 70,
            volatility: 40,
            intelligence: Intel(),
            statisticalContext: null,
            injuryNote: null));

        Assert.Equal(0m, prediction.Edge);
        Assert.Equal(50, prediction.Probability);
        Assert.Equal(PredictionDirection.Over, prediction.Direction);
    }

    [Fact]
    public void High_Confidence_Produces_Stronger_Edge_Than_Low_Confidence()
    {
        var high = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, projectionConfidence: 90, volatility: 30, Intel(usage: 70, health: 80), null, null));
        var low = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, projectionConfidence: 25, volatility: 30, Intel(usage: 70, health: 80), null, null));

        Assert.True(high.Edge > low.Edge);
        Assert.True(high.Confidence > low.Confidence);
        Assert.True(high.Probability >= low.Probability);
    }

    [Fact]
    public void Low_Confidence_Edge_Is_Dampened()
    {
        var prediction = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, projectionConfidence: 20, volatility: 40, Intel(), null, null));

        Assert.Contains(prediction.CalculationNotes, n => n.Contains("dampened", StringComparison.OrdinalIgnoreCase));
        Assert.True(prediction.Edge < 5m);
    }

    [Fact]
    public void High_Volatility_Reduces_Edge_Vs_Low_Volatility()
    {
        var calm = Require(_engine.Evaluate(
            Line(PredictionMarketType.RushingYards, 70.5m),
            90m, 80, volatility: 20, Intel(health: 75), null, null));
        var volatilePick = Require(_engine.Evaluate(
            Line(PredictionMarketType.RushingYards, 70.5m),
            90m, 80, volatility: 90, Intel(health: 75), null, null));

        Assert.True(calm.Edge > volatilePick.Edge);
    }

    [Fact]
    public void Missing_Line_Returns_Null()
    {
        var line = Line(PredictionMarketType.ReceivingYards, null);
        var prediction = _engine.Evaluate(line, 100m, 70, 40, Intel(), null, null);
        Assert.Null(prediction);
    }

    [Fact]
    public void Unavailable_Line_Returns_Null()
    {
        var line = Line(PredictionMarketType.ReceivingYards, 94.5m, PropLineFreshness.Unavailable);
        Assert.Null(_engine.Evaluate(line, 108m, 70, 40, Intel(), null, null));
    }

    [Fact]
    public void Stale_Line_Reduces_Confidence_And_Probability()
    {
        var live = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m, PropLineFreshness.Mock),
            108.2m, 80, 35, Intel(health: 75), null, null));
        var stale = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m, PropLineFreshness.Stale),
            108.2m, 80, 35, Intel(health: 75), null, null));

        Assert.True(stale.Confidence < live.Confidence);
        Assert.True(stale.Probability < live.Probability);
        Assert.Equal(PropLineFreshness.Stale, stale.LineFreshness);
        Assert.Contains("stale", stale.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_Player_Intelligence_Still_Produces_Prediction()
    {
        var prediction = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 75, 40, intelligence: null, statisticalContext: null, injuryNote: null));

        Assert.Equal(PredictionDirection.Over, prediction.Direction);
        Assert.Contains(prediction.SupportingIntelligence, s =>
            s.Contains("No player intelligence", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Playbook projects", prediction.Reasoning, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_Injury_Concern_Tempers_Edge()
    {
        var healthy = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 80, 35, Intel(health: 80), null, injuryNote: null));
        var injured = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 80, 35, Intel(health: 80), null, injuryNote: "Questionable (ankle)"));

        Assert.True(injured.Edge < healthy.Edge);
        Assert.Contains("Injury note", injured.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(injured.CalculationNotes, n => n.Contains("Injury", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Multiple_Markets_For_Same_Player_Produce_Distinct_Predictions()
    {
        var yards = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m, id: "chase-yds"),
            108.2m, 80, 35, Intel(usage: 70), null, null));
        var receptions = Require(_engine.Evaluate(
            Line(PredictionMarketType.Receptions, 6.5m, id: "chase-rec"),
            7.8m, 78, 35, Intel(usage: 70), null, null));

        Assert.NotEqual(yards.Id, receptions.Id);
        Assert.Equal(PredictionMarketType.ReceivingYards, yards.Market);
        Assert.Equal(PredictionMarketType.Receptions, receptions.Market);
        Assert.Equal("Ja'Marr Chase", yards.PlayerName);
        Assert.Equal("Ja'Marr Chase", receptions.PlayerName);
    }

    [Fact]
    public void Prediction_Output_Is_Deterministic()
    {
        var line = Line(PredictionMarketType.ReceivingYards, 94.5m);
        var intel = Intel(usage: 68, health: 72, opportunity: 70);
        var a = Require(_engine.Evaluate(line, 108.2m, 82, 33, intel, null, null));
        var b = Require(_engine.Evaluate(line, 108.2m, 82, 33, intel, null, null));

        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Direction, b.Direction);
        Assert.Equal(a.Edge, b.Edge);
        Assert.Equal(a.Probability, b.Probability);
        Assert.Equal(a.Confidence, b.Confidence);
        Assert.Equal(a.Reasoning, b.Reasoning);
        Assert.Equal(a.CalculationNotes, b.CalculationNotes);
    }

    [Fact]
    public void Human_Reasoning_Omits_Raw_Adjustment_Jargon()
    {
        var prediction = Require(_engine.Evaluate(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 80, 35, Intel(usage: 70, health: 75), null, null));

        Assert.DoesNotContain("Opportunity adjustment", prediction.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Health adjustment", prediction.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quality weight", prediction.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(prediction.CalculationNotes);
    }

    [Fact]
    public void QuickPicksService_Works_Without_Fantasy_League_Dependency()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);

        var ctorParams = typeof(QuickPicksService).GetConstructors().Single().GetParameters();
        Assert.DoesNotContain(ctorParams, p =>
            p.ParameterType.Name.Contains("League", StringComparison.OrdinalIgnoreCase) ||
            p.ParameterType.Name.Contains("Roster", StringComparison.OrdinalIgnoreCase));

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        quickPicks.Refresh();

        var all = quickPicks.GetAllPredictions();
        var top = quickPicks.GetTopPicks();
        var watch = quickPicks.GetWatchPicks();
        var events = quickPicks.GetUpcomingEvents();
        var status = provider.GetRequiredService<IQuickPicksSyncStatus>();

        Assert.NotEmpty(all);
        Assert.NotEmpty(events);
        Assert.True(top.Count + watch.Count > 0);
        Assert.Equal("Mock", status.PropProvider);
        Assert.True(status.PropsLoaded > 0);
        Assert.True(status.PredictionsGenerated > 0);
        Assert.NotNull(status.LastPropSync);
        Assert.NotNull(status.LastPredictionRun);
        Assert.True(status.AveragePredictionConfidence > 0);
        Assert.DoesNotContain(all, p => p.LineFreshness == PropLineFreshness.Unavailable);
        Assert.Contains(all, p => p.LineFreshness == PropLineFreshness.Mock);
        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p.Reasoning)));
    }

    [Fact]
    public async Task Mock_Provider_Includes_Stale_And_Multiple_Chase_Markets()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var lines = await provider.GetRequiredService<MockPropLineProvider>()
            .GetPropLinesAsync();

        Assert.Contains(lines, l => l.Freshness == PropLineFreshness.Stale);
        Assert.True(lines.Count(l => l.PlayerName == "Ja'Marr Chase") >= 2);
        Assert.Contains(lines, l => l.Market == PredictionMarketType.GameTotal);
        Assert.Contains(lines, l => l.Market == PredictionMarketType.Winner);
    }

    [Fact]
    public void Top_Picks_Exclude_Stale_Lines()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        quickPicks.Refresh();

        Assert.All(quickPicks.GetTopPicks(20), p =>
            Assert.True(p.LineFreshness is PropLineFreshness.Live or PropLineFreshness.Mock));
    }

    private static Prediction Require(Prediction? prediction)
    {
        Assert.NotNull(prediction);
        return prediction!;
    }

    private static PropLine Line(
        PredictionMarketType market,
        decimal? line,
        PropLineFreshness freshness = PropLineFreshness.Mock,
        string id = "test-line") =>
        new()
        {
            Id = id,
            Event = new FootballEvent
            {
                EventId = "test-cin-cle",
                HomeTeam = "CLE",
                AwayTeam = "CIN",
                CommenceTime = DateTimeOffset.UtcNow.AddDays(1)
            },
            PlayerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            PlayerName = "Ja'Marr Chase",
            TeamName = "CIN",
            Market = market,
            Line = line,
            Bookmaker = "MockBook",
            Source = "Mock",
            UpdatedAt = DateTimeOffset.UtcNow,
            Freshness = freshness
        };

    private static PlayerIntelligenceProfile Intel(
        int health = 50,
        int opportunity = 50,
        int usage = 50,
        int risk = 10,
        int confidence = 70) =>
        new()
        {
            PlayerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            OverallConfidence = confidence,
            OverallRisk = risk,
            OpportunityScore = opportunity,
            TrendDirection = TrendDirection.Flat,
            HealthScore = health,
            UsageScore = usage,
            NewsMomentum = 50,
            LastUpdated = DateTimeOffset.UtcNow,
            SupportingFacts = [],
            Headline = "Neutral",
            ChangeSignal = IntelligenceChangeSignal.Neutral
        };
}
