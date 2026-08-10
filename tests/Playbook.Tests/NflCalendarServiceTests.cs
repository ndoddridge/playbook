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
    public void Canonical_Season_Has_Exact_Structure()
    {
        var weeks = NflWeekRef.BuildCanonicalSeason(2026);
        Assert.Equal(3 + 18 + 4, weeks.Count);
        Assert.Equal(3, weeks.Count(w => w.Phase == NflSeasonPhase.Preseason));
        Assert.Equal(18, weeks.Count(w => w.Phase == NflSeasonPhase.RegularSeason));
        Assert.Equal(4, weeks.Count(w => w.Phase == NflSeasonPhase.Postseason));
        Assert.DoesNotContain(weeks, w => w.Phase == NflSeasonPhase.Preseason && w.Week > 3);
        Assert.Equal("Conference Championship", weeks.Single(w => w.Phase == NflSeasonPhase.Postseason && w.Week == 3).WeekLabel);
        Assert.Equal("SB", weeks.Single(w => w.Phase == NflSeasonPhase.Postseason && w.Week == 4).ShortLabel);

        // Canonical order: Pre 1–3 → Reg 1–18 → Post rounds
        for (var i = 1; i < weeks.Count; i++)
        {
            Assert.True(NflWeekRef.CompareCanonical(weeks[i - 1], weeks[i]) < 0);
        }
    }

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
    public void SelectActiveWeek_Advances_Within_Same_Phase()
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
            MakeSlate(2026, NflSeasonPhase.Preseason, 1, now.AddDays(-10)),
            MakeSlate(2026, NflSeasonPhase.Preseason, 2, now.AddDays(2))
        };

        var selected = service.SelectActiveWeek(slates, current, preferred: null, utcNow: now);
        Assert.Equal(new NflWeekRef(2026, NflSeasonPhase.Preseason, 2), selected);
    }

    [Fact]
    public void SelectActiveWeek_Does_Not_Jump_To_Future_Regular_Season_During_Preseason()
    {
        var service = CreateService();
        var current = new NflSeasonContext
        {
            Season = 2026,
            Phase = NflSeasonPhase.Preseason,
            Week = 1,
            DisplayWeek = 1
        };

        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var slates = new List<NflSlate>
        {
            MakeSlate(2026, NflSeasonPhase.Preseason, 1, now.AddDays(5)),
            MakeSlate(2026, NflSeasonPhase.RegularSeason, 1, now.AddDays(32)),
            MakeSlate(2026, NflSeasonPhase.RegularSeason, 2, now.AddDays(39))
        };

        var selected = service.SelectActiveWeek(slates, current, preferred: null, utcNow: now);
        Assert.Equal(new NflWeekRef(2026, NflSeasonPhase.Preseason, 1), selected);
    }

    [Fact]
    public void SelectActiveWeek_Uses_Calendar_When_Only_Future_Regular_Provider_Data_Exists()
    {
        var service = CreateService();
        var current = new NflSeasonContext
        {
            Season = 2026,
            Phase = NflSeasonPhase.Preseason,
            Week = 1,
            DisplayWeek = 1
        };

        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var slates = new List<NflSlate>
        {
            MakeSlate(2026, NflSeasonPhase.RegularSeason, 1, now.AddDays(32))
        };

        var selected = service.SelectActiveWeek(slates, current, preferred: null, utcNow: now);
        Assert.Equal(new NflWeekRef(2026, NflSeasonPhase.Preseason, 1), selected);
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
        Assert.Equal(25, quickPicks.CanonicalWeeks.Count);
        Assert.Equal(3, quickPicks.CanonicalWeeks.Count(w => w.Phase == NflSeasonPhase.Preseason));
        Assert.DoesNotContain(
            quickPicks.CanonicalWeeks,
            w => w.Phase == NflSeasonPhase.Preseason && w.Week > 3);

        // Prev/next follows canonical order into a week that may or may not have markets.
        var next = selected.NextInSeason();
        Assert.NotNull(next);
        Assert.True(quickPicks.TrySelectWeek(next!));
        Assert.Equal(next, quickPicks.SelectedWeek);
        Assert.All(quickPicks.GetAllPredictions(), p => Assert.True(next.Matches(p.Event)));

        if (quickPicks.AvailableWeeks.Count > 1)
        {
            var otherMarket = quickPicks.AvailableWeeks.First(w => w != next);
            Assert.True(quickPicks.TrySelectWeek(otherMarket));
            Assert.Equal(otherMarket, quickPicks.SelectedWeek);
            Assert.All(quickPicks.GetAllPredictions(), p => Assert.True(otherMarket.Matches(p.Event)));
        }
    }

    private static NflSlate MakeSlate(
        int season,
        NflSeasonPhase phase,
        int week,
        DateTimeOffset kickoff) =>
        new()
        {
            Ref = new NflWeekRef(season, phase, week),
            Events =
            [
                new FootballEvent
                {
                    EventId = $"{phase}-{week}",
                    HomeTeam = "HOME",
                    AwayTeam = "AWAY",
                    CommenceTime = kickoff,
                    Season = season,
                    Phase = phase,
                    Week = week
                }
            ],
            EarliestKickoff = kickoff,
            LatestKickoff = kickoff
        };

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
