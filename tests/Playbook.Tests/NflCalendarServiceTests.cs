using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Players.Data;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Infrastructure.Predictions;

namespace Playbook.Tests;

public class NflCalendarServiceTests
{
    [Theory]
    [InlineData("pre", NflSeasonPhase.Preseason)]
    [InlineData("regular", NflSeasonPhase.RegularSeason)]
    [InlineData("post", NflSeasonPhase.Postseason)]
    public void ParsePhase_Maps_Sleeper_Values(string raw, NflSeasonPhase expected) =>
        Assert.Equal(expected, NflCalendarService.ParsePhase(raw));

    [Fact]
    public void ResolveWeekNumber_Uses_Phase_Start_Date()
    {
        var ctx = new NflSeasonContext
        {
            Season = 2026,
            Phase = NflSeasonPhase.Preseason,
            Week = 1,
            DisplayWeek = 1,
            PhaseStartDate = new DateOnly(2026, 8, 6)
        };

        var week1 = NflCalendarService.ResolveWeekNumber(
            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero), ctx);
        var week2 = NflCalendarService.ResolveWeekNumber(
            new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero), ctx);

        Assert.Equal(1, week1);
        Assert.Equal(2, week2);
    }

    [Fact]
    public void CalendarFallback_August_Is_Preseason()
    {
        var ctx = NflCalendarService.BuildCalendarFallback(
            new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(2026, ctx.Season);
        Assert.Equal(NflSeasonPhase.Preseason, ctx.Phase);
        Assert.True(ctx.Week >= 1);
    }

    [Fact]
    public void CalendarFallback_Mid_September_Is_Regular_Season()
    {
        var ctx = NflCalendarService.BuildCalendarFallback(
            new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(NflSeasonPhase.RegularSeason, ctx.Phase);
        Assert.True(ctx.Week >= 1);
    }

    [Fact]
    public void SelectActiveWeek_Never_Mixes_When_Current_Present()
    {
        var service = CreateService();
        var current = new NflSeasonContext
        {
            Season = 2026,
            Phase = NflSeasonPhase.Preseason,
            Week = 1,
            DisplayWeek = 1,
            PhaseStartDate = new DateOnly(2026, 8, 6)
        };
        var available = new List<NflWeekRef>
        {
            new(2026, NflSeasonPhase.Preseason, 1),
            new(2026, NflSeasonPhase.Preseason, 2)
        };

        var selected = service.SelectActiveWeek(available, current);
        Assert.Equal(new NflWeekRef(2026, NflSeasonPhase.Preseason, 1), selected);
    }

    [Fact]
    public void EnrichEvents_Tags_Season_Phase_Week()
    {
        var service = CreateService();
        var current = new NflSeasonContext
        {
            Season = 2026,
            Phase = NflSeasonPhase.Preseason,
            Week = 1,
            DisplayWeek = 1,
            PhaseStartDate = new DateOnly(2026, 8, 6)
        };
        var events = new[]
        {
            new FootballEvent
            {
                EventId = "e1",
                HomeTeam = "SEA",
                AwayTeam = "NE",
                CommenceTime = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero)
            }
        };

        var enriched = service.EnrichEvents(events, current);
        Assert.Equal(2026, enriched[0].Season);
        Assert.Equal(NflSeasonPhase.Preseason, enriched[0].Phase);
        Assert.Equal(2, enriched[0].Week);
        Assert.Contains("Preseason", enriched[0].ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NE @ SEA", enriched[0].ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuickPicks_Service_Keeps_Single_Week_Slate()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Mock");

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        quickPicks.Refresh();

        var selected = quickPicks.SelectedWeek;
        Assert.NotNull(selected);
        Assert.All(quickPicks.GetAllPredictions(), p => Assert.True(selected!.Matches(p.Event)));
        Assert.All(quickPicks.GetUpcomingEvents(), e => Assert.True(selected!.Matches(e)));
    }

    private static NflCalendarService CreateService() =>
        new(new SimpleHttpClientFactory(), NullLogger<NflCalendarService>.Instance);

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new()
        {
            BaseAddress = new Uri("https://api.sleeper.app/v1/")
        };
    }
}
