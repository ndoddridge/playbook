using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Infrastructure.Players;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Builds NFL slates from real event kickoff times.
/// Preseason is capped at 3 weeks. Default selection is the next incomplete slate.
/// </summary>
public sealed class NflCalendarService : INflCalendarService
{
    public const string HttpClientName = LivePlayerDataProvider.HttpClientName;

    private static readonly TimeZoneInfo Eastern =
        ResolveEasternTimeZone();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NflCalendarService> _logger;
    private readonly object _gate = new();
    private NflSeasonContext? _cached;
    private DateTimeOffset _cacheExpires = DateTimeOffset.MinValue;

    public NflCalendarService(
        IHttpClientFactory httpClientFactory,
        ILogger<NflCalendarService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public NflSeasonContext GetCurrentContext()
    {
        lock (_gate)
        {
            if (_cached is not null && DateTimeOffset.UtcNow < _cacheExpires)
            {
                return _cached;
            }
        }

        var resolved = ResolveLiveOrFallback();
        lock (_gate)
        {
            _cached = resolved;
            _cacheExpires = DateTimeOffset.UtcNow.AddMinutes(15);
            return _cached;
        }
    }

    public IReadOnlyList<FootballEvent> EnrichEvents(
        IReadOnlyList<FootballEvent> events,
        NflSeasonContext current)
    {
        if (events.Count == 0)
        {
            return events;
        }

        // Assign phase from provider hint when present; otherwise infer from kickoff vs regular start.
        var withPhase = events
            .Select(ev =>
            {
                var phase = ev.PhaseHint
                            ?? InferPhase(ev.CommenceTime, current);
                return Clone(ev, current.Season, phase, week: 0);
            })
            .ToList();

        var enriched = new List<FootballEvent>(withPhase.Count);
        foreach (var phaseGroup in withPhase.GroupBy(e => e.Phase).OrderBy(g => g.Key))
        {
            enriched.AddRange(AssignWeeksInPhase(phaseGroup.ToList(), current.Season, phaseGroup.Key));
        }

        return enriched
            .OrderBy(e => e.CommenceTime)
            .ToList();
    }

    public IReadOnlyList<NflSlate> BuildSlates(IReadOnlyList<FootballEvent> enrichedEvents)
    {
        return enrichedEvents
            .Where(e => e.Season > 0 && e.Week > 0)
            .GroupBy(e => e.WeekRef)
            .Select(g =>
            {
                var ordered = g.OrderBy(e => e.CommenceTime).ToList();
                return new NflSlate
                {
                    Ref = g.Key,
                    Events = ordered,
                    EarliestKickoff = ordered[0].CommenceTime,
                    LatestKickoff = ordered[^1].CommenceTime
                };
            })
            .OrderBy(s => s.Ref.Season)
            .ThenBy(s => s.Ref.Phase)
            .ThenBy(s => s.Ref.Week)
            .ToList();
    }

    public IReadOnlyList<NflWeekRef> GetAvailableWeeks(IReadOnlyList<FootballEvent> events) =>
        BuildSlates(events).Select(s => s.Ref).ToList();

    public NflWeekRef SelectActiveWeek(
        IReadOnlyList<NflSlate> available,
        NflSeasonContext current,
        NflWeekRef? preferred = null,
        DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        if (available.Count == 0)
        {
            return preferred ?? current.CurrentWeekRef;
        }

        if (preferred is not null)
        {
            var preferredSlate = available.FirstOrDefault(s => s.Ref == preferred);
            if (preferredSlate is not null)
            {
                return preferredSlate.Ref;
            }
        }

        // Next relevant slate: first incomplete slate by chronological order.
        var nextOpen = available.FirstOrDefault(s => s.HasUpcomingOrLive(now));
        if (nextOpen is not null)
        {
            return nextOpen.Ref;
        }

        // All complete → stay on the latest available slate.
        return available[^1].Ref;
    }

    /// <summary>
    /// Cluster games inside one phase into week/round numbers using Eastern Tuesday week starts.
    /// Preseason capped at 3; regular at 18; postseason at 4 rounds.
    /// </summary>
    public static IReadOnlyList<FootballEvent> AssignWeeksInPhase(
        IReadOnlyList<FootballEvent> phaseEvents,
        int season,
        NflSeasonPhase phase)
    {
        if (phaseEvents.Count == 0)
        {
            return phaseEvents;
        }

        var ordered = phaseEvents.OrderBy(e => e.CommenceTime).ToList();
        var weekStarts = ordered
            .Select(e => NflWeekStartEastern(e.CommenceTime))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var maxWeeks = phase switch
        {
            NflSeasonPhase.Preseason => NflWeekRef.MaxPreseasonWeeks,
            NflSeasonPhase.Postseason => NflWeekRef.MaxPostseasonRounds,
            _ => NflWeekRef.MaxRegularSeasonWeeks
        };

        // Map each distinct week-start to sequential week numbers 1..n (capped).
        var startToWeek = new Dictionary<DateOnly, int>();
        for (var i = 0; i < weekStarts.Count; i++)
        {
            startToWeek[weekStarts[i]] = Math.Min(i + 1, maxWeeks);
        }

        return ordered
            .Select(ev =>
            {
                var start = NflWeekStartEastern(ev.CommenceTime);
                var week = startToWeek[start];
                return Clone(ev, season, phase, week);
            })
            .ToList();
    }

    public static DateOnly NflWeekStartEastern(DateTimeOffset commenceTime)
    {
        var et = TimeZoneInfo.ConvertTime(commenceTime, Eastern);
        var date = DateOnly.FromDateTime(et.DateTime);
        // NFL weeks roll on Tuesday.
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7;
        return date.AddDays(-diff);
    }

    public static NflSeasonPhase InferPhase(DateTimeOffset commenceTime, NflSeasonContext current)
    {
        // Prefer live provider phase when the game is near "now"; otherwise use kickoff vs regular start.
        if (current.RegularSeasonStartDate is DateOnly regStart)
        {
            var gameDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(commenceTime, Eastern).DateTime);
            if (gameDate < regStart)
            {
                return NflSeasonPhase.Preseason;
            }

            // Rough postseason window: after ~18 weeks from regular start.
            var postStart = regStart.AddDays(18 * 7);
            if (gameDate >= postStart)
            {
                return NflSeasonPhase.Postseason;
            }

            return NflSeasonPhase.RegularSeason;
        }

        return current.Phase;
    }

    public static NflSeasonPhase ParsePhase(string? seasonType) =>
        (seasonType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "pre" or "preseason" => NflSeasonPhase.Preseason,
            "post" or "postseason" or "playoffs" => NflSeasonPhase.Postseason,
            _ => NflSeasonPhase.RegularSeason
        };

    public static NflSeasonContext BuildCalendarFallback(DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var year = now.Year;
        var month = now.Month;
        var day = now.Day;

        if (month is 1 || (month == 2 && day <= 15))
        {
            return new NflSeasonContext
            {
                Season = year - 1,
                Phase = NflSeasonPhase.Postseason,
                Week = 1,
                DisplayWeek = 1,
                PhaseStartDate = new DateOnly(year, 1, 10),
                RegularSeasonStartDate = new DateOnly(year - 1, 9, 4),
                ResolvedAt = now,
                Source = "CalendarFallback"
            };
        }

        if (month is >= 2 and <= 7)
        {
            return new NflSeasonContext
            {
                Season = year,
                Phase = NflSeasonPhase.Preseason,
                Week = 1,
                DisplayWeek = 1,
                PhaseStartDate = new DateOnly(year, 8, 1),
                RegularSeasonStartDate = new DateOnly(year, 9, 10),
                ResolvedAt = now,
                Source = "CalendarFallback"
            };
        }

        if (month == 8 || (month == 9 && day < 10))
        {
            return new NflSeasonContext
            {
                Season = year,
                Phase = NflSeasonPhase.Preseason,
                Week = 1,
                DisplayWeek = 1,
                PhaseStartDate = new DateOnly(year, 8, 1),
                RegularSeasonStartDate = new DateOnly(year, 9, 10),
                ResolvedAt = now,
                Source = "CalendarFallback"
            };
        }

        return new NflSeasonContext
        {
            Season = year,
            Phase = NflSeasonPhase.RegularSeason,
            Week = 1,
            DisplayWeek = 1,
            PhaseStartDate = new DateOnly(year, 9, 10),
            RegularSeasonStartDate = new DateOnly(year, 9, 10),
            ResolvedAt = now,
            Source = "CalendarFallback"
        };
    }

    private NflSeasonContext ResolveLiveOrFallback()
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var state = client.GetFromJsonAsync<SleeperNflState>("state/nfl")
                .GetAwaiter()
                .GetResult();
            if (state is not null && int.TryParse(state.Season, out var season))
            {
                var phase = ParsePhase(state.SeasonType);
                var week = state.Week > 0 ? state.Week : Math.Max(1, state.DisplayWeek);
                if (phase == NflSeasonPhase.Preseason)
                {
                    week = Math.Clamp(week, 1, NflWeekRef.MaxPreseasonWeeks);
                }
                else if (phase == NflSeasonPhase.RegularSeason)
                {
                    week = Math.Clamp(week, 1, NflWeekRef.MaxRegularSeasonWeeks);
                }
                else
                {
                    week = Math.Clamp(week, 1, NflWeekRef.MaxPostseasonRounds);
                }

                DateOnly? phaseStart = null;
                if (!string.IsNullOrWhiteSpace(state.SeasonStartDate) &&
                    DateOnly.TryParse(state.SeasonStartDate, out var parsed))
                {
                    phaseStart = parsed;
                }

                // During preseason, Sleeper's season_start_date is the pre start.
                // Regular-season open is derived later from the first regular Odds events when available.
                DateOnly? regularStart = phase == NflSeasonPhase.RegularSeason
                    ? phaseStart
                    : null;

                _logger.LogInformation(
                    "NFL calendar: {Season} {Phase} week {Week} (display {Display}) phaseStart={Start}",
                    season,
                    phase,
                    week,
                    state.DisplayWeek,
                    phaseStart?.ToString("yyyy-MM-dd") ?? "—");

                return new NflSeasonContext
                {
                    Season = season,
                    Phase = phase,
                    Week = week,
                    DisplayWeek = state.DisplayWeek > 0 ? state.DisplayWeek : week,
                    PhaseStartDate = phaseStart,
                    RegularSeasonStartDate = regularStart,
                    ResolvedAt = DateTimeOffset.UtcNow,
                    Source = "Sleeper"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to resolve live NFL state; using calendar fallback");
        }

        return BuildCalendarFallback();
    }

    private static FootballEvent Clone(
        FootballEvent ev,
        int season,
        NflSeasonPhase phase,
        int week) =>
        new()
        {
            EventId = ev.EventId,
            HomeTeam = ev.HomeTeam,
            AwayTeam = ev.AwayTeam,
            CommenceTime = ev.CommenceTime,
            Season = season,
            Phase = phase,
            Week = week,
            PhaseHint = ev.PhaseHint ?? phase
        };

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    private sealed class SleeperNflState
    {
        [JsonPropertyName("season")]
        public string? Season { get; set; }

        [JsonPropertyName("season_type")]
        public string? SeasonType { get; set; }

        [JsonPropertyName("week")]
        public int Week { get; set; }

        [JsonPropertyName("display_week")]
        public int DisplayWeek { get; set; }

        [JsonPropertyName("season_start_date")]
        public string? SeasonStartDate { get; set; }
    }
}
