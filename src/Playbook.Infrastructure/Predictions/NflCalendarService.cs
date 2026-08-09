using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Infrastructure.Players;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Resolves NFL season/phase/week from Sleeper state/nfl and maps kickoffs → week numbers.
/// Automatically follows pre → regular → post transitions via the live state payload.
/// </summary>
public sealed class NflCalendarService : INflCalendarService
{
    public const string HttpClientName = LivePlayerDataProvider.HttpClientName;

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

        return events
            .Select(ev => EnrichOne(ev, current))
            .ToList();
    }

    public IReadOnlyList<NflWeekRef> GetAvailableWeeks(IReadOnlyList<FootballEvent> events) =>
        events
            .Where(e => e.Season > 0 && e.Week > 0)
            .Select(e => e.WeekRef)
            .Distinct()
            .OrderBy(w => w.Season)
            .ThenBy(w => w.Phase)
            .ThenBy(w => w.Week)
            .ToList();

    public NflWeekRef SelectActiveWeek(
        IReadOnlyList<NflWeekRef> available,
        NflSeasonContext current,
        NflWeekRef? preferred = null)
    {
        if (available.Count == 0)
        {
            return preferred ?? current.CurrentWeekRef;
        }

        if (preferred is not null && available.Contains(preferred))
        {
            return preferred;
        }

        var currentRef = current.CurrentWeekRef;
        if (available.Contains(currentRef))
        {
            return currentRef;
        }

        // Prefer the soonest week at/after current, else the latest available.
        var atOrAfter = available
            .Where(w =>
                w.Season > currentRef.Season ||
                (w.Season == currentRef.Season && w.Phase > currentRef.Phase) ||
                (w.Season == currentRef.Season && w.Phase == currentRef.Phase && w.Week >= currentRef.Week))
            .OrderBy(w => w.Season)
            .ThenBy(w => w.Phase)
            .ThenBy(w => w.Week)
            .FirstOrDefault();

        return atOrAfter ?? available[^1];
    }

    private FootballEvent EnrichOne(FootballEvent ev, NflSeasonContext current)
    {
        var week = ResolveWeekNumber(ev.CommenceTime, current);
        return new FootballEvent
        {
            EventId = ev.EventId,
            HomeTeam = ev.HomeTeam,
            AwayTeam = ev.AwayTeam,
            CommenceTime = ev.CommenceTime,
            Season = current.Season,
            Phase = current.Phase,
            Week = week
        };
    }

    /// <summary>
    /// Map kickoff → week using the provider phase-start date when available.
    /// Falls back to anchoring around "now" + current week so we never hardcode a season calendar.
    /// </summary>
    public static int ResolveWeekNumber(DateTimeOffset commenceTime, NflSeasonContext current)
    {
        var gameDate = DateOnly.FromDateTime(commenceTime.UtcDateTime);

        if (current.PhaseStartDate is DateOnly start)
        {
            var days = gameDate.DayNumber - start.DayNumber;
            if (days < 0)
            {
                // Slightly before published start — still treat as week 1 of the active phase.
                return Math.Max(1, current.Week > 0 ? current.Week : 1);
            }

            return Math.Clamp((days / 7) + 1, 1, current.Phase == NflSeasonPhase.Preseason ? 5 : 22);
        }

        // Anchor: current week contains "today"; offset by whole weeks from UTC today.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var deltaDays = gameDate.DayNumber - today.DayNumber;
        var weekOffset = (int)Math.Floor(deltaDays / 7.0);
        var anchored = (current.Week > 0 ? current.Week : Math.Max(1, current.DisplayWeek)) + weekOffset;
        return Math.Clamp(anchored, 1, 22);
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
                DateOnly? start = null;
                if (!string.IsNullOrWhiteSpace(state.SeasonStartDate) &&
                    DateOnly.TryParse(state.SeasonStartDate, out var parsed))
                {
                    start = parsed;
                }

                _logger.LogInformation(
                    "NFL calendar: {Season} {Phase} week {Week} (display {Display}) start={Start}",
                    season,
                    phase,
                    week,
                    state.DisplayWeek,
                    start?.ToString("yyyy-MM-dd") ?? "—");

                return new NflSeasonContext
                {
                    Season = season,
                    Phase = phase,
                    Week = week,
                    DisplayWeek = state.DisplayWeek > 0 ? state.DisplayWeek : week,
                    PhaseStartDate = start,
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

    /// <summary>
    /// Date-based fallback that still transitions phases without hardcoding a single "today" slate.
    /// Approximate NFL windows: pre Aug–early Sep, regular Sep–early Jan, post through mid-Feb.
    /// </summary>
    public static NflSeasonContext BuildCalendarFallback(DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var year = now.Year;
        var month = now.Month;
        var day = now.Day;

        // Jan–Feb: prior season postseason (or offseason → next pre).
        if (month is 1 || (month == 2 && day <= 15))
        {
            return new NflSeasonContext
            {
                Season = year - 1,
                Phase = NflSeasonPhase.Postseason,
                Week = month == 1 ? Math.Clamp((day / 7) + 1, 1, 5) : 5,
                DisplayWeek = month == 1 ? Math.Clamp((day / 7) + 1, 1, 5) : 5,
                PhaseStartDate = new DateOnly(year, 1, 10),
                ResolvedAt = now,
                Source = "CalendarFallback"
            };
        }

        if (month is >= 2 and <= 7 || (month == 8 && day < 1))
        {
            // Offseason → upcoming preseason of this calendar year.
            return new NflSeasonContext
            {
                Season = year,
                Phase = NflSeasonPhase.Preseason,
                Week = 1,
                DisplayWeek = 1,
                PhaseStartDate = new DateOnly(year, 8, 1),
                ResolvedAt = now,
                Source = "CalendarFallback"
            };
        }

        if (month == 8 || (month == 9 && day < 4))
        {
            var start = new DateOnly(year, 8, 1);
            var week = Math.Clamp((DateOnly.FromDateTime(now.UtcDateTime).DayNumber - start.DayNumber) / 7 + 1, 1, 4);
            return new NflSeasonContext
            {
                Season = year,
                Phase = NflSeasonPhase.Preseason,
                Week = week,
                DisplayWeek = week,
                PhaseStartDate = start,
                ResolvedAt = now,
                Source = "CalendarFallback"
            };
        }

        var regStart = new DateOnly(year, 9, 4);
        var regWeek = Math.Clamp(
            (DateOnly.FromDateTime(now.UtcDateTime).DayNumber - regStart.DayNumber) / 7 + 1,
            1,
            18);
        return new NflSeasonContext
        {
            Season = year,
            Phase = NflSeasonPhase.RegularSeason,
            Week = regWeek,
            DisplayWeek = regWeek,
            PhaseStartDate = regStart,
            ResolvedAt = now,
            Source = "CalendarFallback"
        };
    }

    public static NflSeasonPhase ParsePhase(string? seasonType) =>
        (seasonType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "pre" or "preseason" => NflSeasonPhase.Preseason,
            "post" or "postseason" or "playoffs" => NflSeasonPhase.Postseason,
            _ => NflSeasonPhase.RegularSeason
        };

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
