using Playbook.Application.Players;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Players;
using Playbook.Core.Predictions;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Deterministic mock NFL prop lines for local development without an Odds API key.
///
/// The fixture derives its kickoffs and phase from the live calendar rather than pinning them
/// to a fixed weekday and a hardcoded phase. Two things previously drifted:
///
///   1. PHASE. Events were hardcoded Preseason. Once the real calendar moves to the regular
///      season, SelectActiveWeek's phase-first rule discards them entirely.
///   2. WEEK NUMBERING. AssignWeeksInPhase numbers a provider's own kickoff clusters
///      sequentially 1..n; it does not use absolute calendar weeks. Emitting two clusters meant
///      they became "week 1" and "week 2" of the mock's data, and SelectActiveWeek then matched
///      whichever happened to equal the real current week number. As the wall clock advanced the
///      selected cluster changed, so the visible fixture silently varied over time.
///
/// All events are therefore emitted in a SINGLE upcoming cluster carrying the calendar's current
/// phase, which makes the selected slate deterministic regardless of the date the code runs.
/// </summary>
public sealed class MockPropLineProvider : IPropLineProvider
{
    private readonly IPlayerService _players;
    private readonly INflCalendarService _calendar;

    public MockPropLineProvider(IPlayerService players, INflCalendarService calendar)
    {
        _players = players;
        _calendar = calendar;
    }

    public string ProviderName => "Mock";

    public Task<IReadOnlyList<PropLine>> GetPropLinesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var phase = _calendar.GetCurrentContext().Phase;

        // One cluster, always upcoming. Spread over a few hours rather than days so every event
        // shares a single NFL week-start and the provider yields exactly one slate.
        var kickoff = now.AddHours(6);
        if (NflCalendarService.NflWeekStartEastern(kickoff)
            != NflCalendarService.NflWeekStartEastern(kickoff.AddHours(2)))
        {
            // Straddling a week boundary would split the cluster in two; push past it.
            kickoff = kickoff.AddHours(3);
        }

        var eventA = Event("mock-cin-cle", "CLE", "CIN", kickoff, phase);
        var eventB = Event("mock-phi-dal", "DAL", "PHI", kickoff.AddHours(1), phase);
        var eventC = Event("mock-buf-nyj", "NYJ", "BUF", kickoff.AddHours(2), phase);

        var players = _players.GetAllPlayers();
        PropLine? PlayerLine(
            string id,
            FootballEvent ev,
            string fullName,
            PredictionMarketType market,
            decimal line)
        {
            var player = players.FirstOrDefault(p =>
                string.Equals(p.FullName, fullName, StringComparison.OrdinalIgnoreCase));
            return new PropLine
            {
                Id = id,
                Event = ev,
                PlayerId = player?.Id,
                PlayerName = player?.FullName ?? fullName,
                TeamName = player?.Team,
                Market = market,
                Line = line,
                Bookmaker = "MockBook",
                Source = "Mock",
                UpdatedAt = now,
                Freshness = PropLineFreshness.Mock,
                AmericanOddsOver = -110,
                AmericanOddsUnder = -110
            };
        }

        var lines = new List<PropLine>
        {
            PlayerLine("mock-chase-rec-yds", eventA, "Ja'Marr Chase", PredictionMarketType.ReceivingYards, 94.5m)!,
            PlayerLine("mock-chase-rec", eventA, "Ja'Marr Chase", PredictionMarketType.Receptions, 6.5m)!,
            PlayerLine("mock-chase-td", eventA, "Ja'Marr Chase", PredictionMarketType.AnytimeTouchdown, 0.5m)!,
            PlayerLine("mock-burrow-pass", eventA, "Joe Burrow", PredictionMarketType.PassingYards, 265.5m)!,
            PlayerLine("mock-barkley-rush", eventB, "Saquon Barkley", PredictionMarketType.RushingYards, 82.5m)!,
            PlayerLine("mock-lamb-rec-yds", eventB, "CeeDee Lamb", PredictionMarketType.ReceivingYards, 88.5m)!,
            PlayerLine("mock-mahomes-pass", eventB, "Patrick Mahomes", PredictionMarketType.PassingYards, 278.5m)!,
            PlayerLine("mock-allen-pass", eventC, "Josh Allen", PredictionMarketType.PassingYards, 267.5m)!,
            new PropLine
            {
                Id = "mock-stale-gibbs-rush",
                Event = eventB,
                PlayerId = players.FirstOrDefault(p => p.FullName == "Jahmyr Gibbs")?.Id,
                PlayerName = "Jahmyr Gibbs",
                TeamName = "DET",
                Market = PredictionMarketType.RushingYards,
                Line = 70.5m,
                Bookmaker = "MockBook",
                Source = "Mock",
                UpdatedAt = now.AddHours(-12),
                Freshness = PropLineFreshness.Stale,
                AmericanOddsOver = -115,
                AmericanOddsUnder = -105
            },
            new PropLine
            {
                Id = "mock-game-total-cin-cle",
                Event = eventA,
                Market = PredictionMarketType.GameTotal,
                Line = 44.5m,
                Bookmaker = "MockBook",
                Source = "Mock",
                UpdatedAt = now,
                Freshness = PropLineFreshness.Mock,
                AmericanOddsOver = -108,
                AmericanOddsUnder = -112
            },
            new PropLine
            {
                Id = "mock-spread-cin-cle",
                Event = eventA,
                TeamName = "CIN",
                Market = PredictionMarketType.Spread,
                Line = -3.5m,
                Bookmaker = "MockBook",
                Source = "Mock",
                UpdatedAt = now,
                Freshness = PropLineFreshness.Mock
            },
            new PropLine
            {
                Id = "mock-ml-cin-cle",
                Event = eventA,
                TeamName = "CIN",
                Market = PredictionMarketType.Winner,
                Bookmaker = "MockBook",
                Source = "Mock",
                UpdatedAt = now,
                Freshness = PropLineFreshness.Mock,
                AmericanOddsOver = -150,
                AmericanOddsUnder = 130
            },
            new PropLine
            {
                Id = "mock-team-total-cin",
                Event = eventA,
                TeamName = "CIN",
                Market = PredictionMarketType.TeamTotal,
                Line = 23.5m,
                Bookmaker = "MockBook",
                Source = "Mock",
                UpdatedAt = now,
                Freshness = PropLineFreshness.Mock
            },
            new PropLine
            {
                Id = "mock-unknown-rec-yds",
                Event = eventA,
                PlayerName = "Unknown Receiver",
                TeamName = "CIN",
                Market = PredictionMarketType.ReceivingYards,
                Line = 55.5m,
                Bookmaker = "MockBook",
                Source = "Mock",
                UpdatedAt = now,
                Freshness = PropLineFreshness.Mock
            }
        };

        return Task.FromResult<IReadOnlyList<PropLine>>(lines);
    }

    private static FootballEvent Event(
        string id, string home, string away, DateTimeOffset kickoff, NflSeasonPhase phase) =>
        new()
        {
            EventId = id,
            HomeTeam = home,
            AwayTeam = away,
            CommenceTime = kickoff,
            PhaseHint = phase,
            Phase = phase
        };

}
