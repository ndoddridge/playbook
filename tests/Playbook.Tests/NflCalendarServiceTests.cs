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
    public void Preseason_Weeks_Cap_At_Three()
    {
        var events = new List<FootballEvent>();
        // Four Tuesday-spaced clusters — must still cap at week 3.
        for (var i = 0; i < 4; i++)
        {
            events.Add(new FootballEvent
            {
                EventId = $"e{i}",
                HomeTeam = "HOME",
                AwayTeam = "AWAY",
                CommenceTime = new DateTimeOffset(2026, 8, 13, 20, 0, 0, TimeSpan.Zero).AddDays(i * 7),
                PhaseHint = NflSeasonPhase.Preseason,
                Phase = NflSeasonPhase.Preseason
            });
        }

        var enriched = NflCalendarService.AssignWeeksInPhase(events, 2026, NflSeasonPhase.Preseason);
        Assert.Equal(3, enriched.Max(e => e.Week));
        Assert.DoesNotContain(enriched, e => e.Week > 3);
    }

    [Fact]
    public void EnrichEvents_Uses_PhaseHint_Not_Sleeper_Phase_Alone()
    {
        var service = CreateService();
        var current = new NflSeasonContext
        {
            Season = 2026,
            Phase = NflSeasonPhase.Preseason,
            Week = 1,
            DisplayWeek = 1,
            PhaseStartDate = new DateOnly(2026, 8, 6),
            RegularSeasonStartDate = new DateOnly(2026, 9, 8)
        };

        var events = new[]
        {
            new FootballEvent
            {
                EventId = "pre1",
                HomeTeam = "CIN",
                AwayTeam = "DET",
                CommenceTime = new DateTimeOffset(2026, 8, 13, 23, 0, 0, TimeSpan.Zero),
                PhaseHint = NflSeasonPhase.Preseason
            },
            new FootballEvent
            {
                EventId = "reg1",
                HomeTeam = "SEA",
                AwayTeam = "NE",
                CommenceTime = new DateTimeOffset(2026, 9, 10, 0, 15, 0, TimeSpan.Zero),
                PhaseHint = NflSeasonPhase.RegularSeason
            }
        };

        var enriched = service.EnrichEvents(events, current);
        var pre = enriched.Single(e => e.EventId == "pre1");
        var reg = enriched.Single(e => e.EventId == "reg1");

        Assert.Equal(NflSeasonPhase.Preseason, pre.Phase);
        Assert.Equal(1, pre.Week);
        Assert.Equal(NflSeasonPhase.RegularSeason, reg.Phase);
        Assert.Equal(1, reg.Week);
        Assert.DoesNotContain(enriched, e => e.Phase == NflSeasonPhase.Preseason && e.Week > 3);
    }

    [Fact]
    public void SelectActiveWeek_Picks_Next_Incomplete_Slate()
    {
        var service = CreateService();
        var current = new NflSeasonContext
        {
            Season = 2026,
            Phase = NflSeasonPhase.Preseason,
            Week = 1,
            DisplayWeek = 1
        };

        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var slates = new List<NflSlate>
        {
            new()
            {
                Ref = new NflWeekRef(2026, NflSeasonPhase.Preseason, 1),
                Events =
                [
                    new FootballEvent
                    {
                        EventId = "old",
                        HomeTeam = "A",
                        AwayTeam = "B",
                        CommenceTime = now.AddDays(-10),
                        Season = 2026,
                        Phase = NflSeasonPhase.Preseason,
                        Week = 1
                    }
                ],
                EarliestKickoff = now.AddDays(-10),
                LatestKickoff = now.AddDays(-10)
            },
            new()
            {
                Ref = new NflWeekRef(2026, NflSeasonPhase.Preseason, 2),
                Events =
                [
                    new FootballEvent
                    {
                        EventId = "next",
                        HomeTeam = "C",
                        AwayTeam = "D",
                        CommenceTime = now.AddDays(2),
                        Season = 2026,
                        Phase = NflSeasonPhase.Preseason,
                        Week = 2
                    }
                ],
                EarliestKickoff = now.AddDays(2),
                LatestKickoff = now.AddDays(2)
            }
        };

        var selected = service.SelectActiveWeek(slates, current, preferred: null, utcNow: now);
        Assert.Equal(new NflWeekRef(2026, NflSeasonPhase.Preseason, 2), selected);
    }

    [Fact]
    public void CalendarFallback_August_Is_Preseason()
    {
        var ctx = NflCalendarService.BuildCalendarFallback(
            new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(2026, ctx.Season);
        Assert.Equal(NflSeasonPhase.Preseason, ctx.Phase);
    }

    [Fact]
    public void QuickPicks_Service_Keeps_Single_Week_Slate_And_Supports_Navigation()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Mock");

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        quickPicks.Refresh();

        var selected = quickPicks.SelectedWeek;
        Assert.NotNull(selected);
        Assert.True(selected!.Week <= 3 || selected.Phase != NflSeasonPhase.Preseason);
        Assert.All(quickPicks.GetAllPredictions(), p => Assert.True(selected.Matches(p.Event)));
        Assert.NotEmpty(quickPicks.AvailableSlates);

        if (quickPicks.AvailableWeeks.Count > 1)
        {
            var other = quickPicks.AvailableWeeks.First(w => w != selected);
            Assert.True(quickPicks.TrySelectWeek(other));
            Assert.Equal(other, quickPicks.SelectedWeek);
            Assert.All(quickPicks.GetAllPredictions(), p => Assert.True(other.Matches(p.Event)));
        }
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
