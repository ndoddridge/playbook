using Playbook.Application.Players.Data;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Infrastructure.Predictions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Playbook.Tests;

public class PropLineProviderTests
{
    [Fact]
    public void Mock_Provider_Loads_Lines_And_Predictions()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Mock");

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        var status = provider.GetRequiredService<IQuickPicksSyncStatus>();
        quickPicks.Refresh();

        var all = quickPicks.GetAllPredictions();
        Assert.NotEmpty(all);
        Assert.Equal("Mock", status.ConfiguredProvider);
        Assert.Equal("Mock", status.PropProvider);
        Assert.Equal("Mock", status.ProviderStatus);
        Assert.False(status.UsedFallback);
        Assert.Null(status.LastError);
        Assert.True(status.PropsLoaded > 0);
        Assert.True(status.MarketsLoaded > 0);
        Assert.NotNull(status.LastPropSync);
        Assert.NotNull(status.ProviderResponseTime);
        Assert.Contains(all, p => p.LineFreshness == PropLineFreshness.Mock);
        Assert.DoesNotContain(all, p => p.LineFreshness == PropLineFreshness.Live);
        Assert.All(all, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.MarketLabel));
            Assert.False(string.IsNullOrWhiteSpace(p.Reasoning));
            Assert.False(string.IsNullOrWhiteSpace(p.Source));
        });
    }

    [Fact]
    public void Live_Without_Api_Key_Falls_Back_To_Mock()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Live",
            oddsApiKey: "");

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        var status = provider.GetRequiredService<IQuickPicksSyncStatus>();
        quickPicks.Refresh();

        Assert.NotEmpty(quickPicks.GetAllPredictions());
        Assert.Equal("Live", status.ConfiguredProvider);
        Assert.Equal("Mock", status.PropProvider);
        Assert.Equal("Fallback", status.ProviderStatus);
        Assert.True(status.UsedFallback);
        Assert.False(string.IsNullOrWhiteSpace(status.LastError));
        Assert.Contains("ApiKey", status.LastError!, StringComparison.OrdinalIgnoreCase);
        Assert.All(quickPicks.GetAllPredictions(), p =>
            Assert.True(p.LineFreshness is PropLineFreshness.Mock or PropLineFreshness.Stale));
    }

    [Fact]
    public void Live_With_Invalid_Key_Against_Real_Host_Falls_Back()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Live",
            oddsApiKey: "test-invalid-key-not-real",
            oddsApiBaseUrl: "https://api.the-odds-api.com/v4/");

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        var status = provider.GetRequiredService<IQuickPicksSyncStatus>();
        quickPicks.Refresh();

        Assert.NotEmpty(quickPicks.GetAllPredictions());
        Assert.Equal("Live", status.ConfiguredProvider);
        Assert.True(status.UsedFallback);
        Assert.Equal("Fallback", status.ProviderStatus);
        Assert.Equal("Mock", status.PropProvider);
        Assert.False(string.IsNullOrWhiteSpace(status.LastError));
        Assert.True(status.ProviderResponseTime is { TotalMilliseconds: >= 0 });
    }

    [Fact]
    public void Live_Provider_Failure_Falls_Back_To_Mock()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Live",
            oddsApiKey: "any-key",
            oddsApiBaseUrl: "http://127.0.0.1:1/");

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        var status = provider.GetRequiredService<IQuickPicksSyncStatus>();
        quickPicks.Refresh();

        Assert.NotEmpty(quickPicks.GetAllPredictions());
        Assert.Equal("Live", status.ConfiguredProvider);
        Assert.Equal("Mock", status.PropProvider);
        Assert.Equal("Fallback", status.ProviderStatus);
        Assert.True(status.UsedFallback);
        Assert.False(string.IsNullOrWhiteSpace(status.LastError));
    }

    [Fact]
    public void Live_Without_Api_Key_And_Fallback_Disabled_Reports_Unavailable_Instead_Of_Mock()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Live",
            oddsApiKey: "",
            allowMockFallback: false);

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        var status = provider.GetRequiredService<IQuickPicksSyncStatus>();
        quickPicks.Refresh();

        // Never presents mock lines as real when live is unavailable and fallback is disabled.
        Assert.Empty(quickPicks.GetAllPredictions());
        Assert.False(status.UsedFallback);
        Assert.NotEqual("Mock", status.PropProvider);
        Assert.False(string.IsNullOrWhiteSpace(status.LastError));
        Assert.Contains("ApiKey", status.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Live_Provider_Failure_With_Fallback_Disabled_Reports_Unavailable_Instead_Of_Mock()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Live",
            oddsApiKey: "any-key",
            oddsApiBaseUrl: "http://127.0.0.1:1/",
            allowMockFallback: false);

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        var status = provider.GetRequiredService<IQuickPicksSyncStatus>();
        quickPicks.Refresh();

        Assert.Empty(quickPicks.GetAllPredictions());
        Assert.False(status.UsedFallback);
        Assert.NotEqual("Mock", status.PropProvider);
        Assert.False(string.IsNullOrWhiteSpace(status.LastError));
    }

    [Fact]
    public void Explicit_Mock_Provider_Ignores_AllowMockFallback_Flag()
    {
        // Provider=Mock is a deliberate dev choice, not a fallback — always honored.
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Mock",
            allowMockFallback: false);

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        var status = provider.GetRequiredService<IQuickPicksSyncStatus>();
        quickPicks.Refresh();

        Assert.NotEmpty(quickPicks.GetAllPredictions());
        Assert.Equal("Mock", status.PropProvider);
        Assert.False(status.UsedFallback);
    }

    [Fact]
    public void Live_Configuration_Registers_TheOddsApi_Provider()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Live",
            oddsApiKey: "configured-key");

        var providers = provider.GetServices<IPropLineProvider>().ToList();
        Assert.Contains(providers, p => p is MockPropLineProvider);
        Assert.Contains(providers, p => p is LivePropLineProvider);
        Assert.Equal("TheOddsAPI", providers.OfType<LivePropLineProvider>().Single().ProviderName);

        // Resolving the service applies configured provider onto sync status.
        _ = provider.GetRequiredService<IQuickPicksService>();
        var status = provider.GetRequiredService<IQuickPicksSyncStatus>();
        Assert.Equal("Live", status.ConfiguredProvider);
    }

    [Fact]
    public void Stale_Lines_Never_Produce_A_Prediction()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Mock");

        // The mock fixture deliberately includes one stale line to exercise this path.
        var mockProvider = provider.GetServices<IPropLineProvider>().OfType<MockPropLineProvider>().Single();
        var rawLines = mockProvider.GetPropLinesAsync().GetAwaiter().GetResult();
        Assert.Contains(rawLines, l => l.Freshness == PropLineFreshness.Stale);

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        quickPicks.Refresh();

        Assert.DoesNotContain(
            quickPicks.GetAllPredictions(),
            p => p.LineFreshness == PropLineFreshness.Stale);
        Assert.All(quickPicks.GetAllPredictions(), p =>
            Assert.True(p.LineFreshness is PropLineFreshness.Live or PropLineFreshness.Mock));
    }

    [Fact]
    public void Missing_Line_Does_Not_Produce_Prediction()
    {
        var engine = new QuickPicksEngine(Options.Create(new QuickPicksScoringOptions()));
        var line = new PropLine
        {
            Id = "missing-line",
            Event = new FootballEvent
            {
                EventId = "e1",
                HomeTeam = "CLE",
                AwayTeam = "CIN",
                CommenceTime = DateTimeOffset.UtcNow.AddDays(1)
            },
            PlayerName = "Ja'Marr Chase",
            Market = PredictionMarketType.ReceivingYards,
            Line = null,
            Bookmaker = "MockBook",
            Source = "Mock",
            UpdatedAt = DateTimeOffset.UtcNow,
            Freshness = PropLineFreshness.Mock
        };

        Assert.Null(engine.Evaluate(new QuickPickEvaluationContext
        {
            Line = line,
            PlaybookProjection = 100m,
            ProjectionConfidence = 70,
            Volatility = 40
        }));
    }

    [Fact]
    public void QuickPicks_Still_Calculates_Edge_With_Mock_Lines()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Mock");

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        quickPicks.Refresh();
        var picks = quickPicks.GetAllPredictions();

        Assert.Contains(picks, p => p.Probability is >= 15 and <= 88);
        Assert.Contains(picks, p => p.Confidence is >= 12 and <= 92);
        Assert.Contains(picks, p => p.PlaybookProjection is not null || p.Market is PredictionMarketType.Winner);
        Assert.All(picks, p => Assert.NotEqual(default, p.LastUpdated));
    }
}
