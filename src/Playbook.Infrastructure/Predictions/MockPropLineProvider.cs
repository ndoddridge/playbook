using Playbook.Application.Players;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Players;
using Playbook.Core.Predictions;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Deterministic mock NFL prop lines for local development without an Odds API key.
/// Includes two preseason slates so week navigation can be exercised offline.
/// </summary>
public sealed class MockPropLineProvider : IPropLineProvider
{
    private readonly IPlayerService _players;

    public MockPropLineProvider(IPlayerService players)
    {
        _players = players;
    }

    public string ProviderName => "Mock";

    public Task<IReadOnlyList<PropLine>> GetPropLinesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        // Anchor mock kickoffs on upcoming Thu 20:00 UTC so they land in one NFL week cluster.
        var week1Kickoff = NextThursday(now).AddHours(20);
        var week2Kickoff = week1Kickoff.AddDays(7);

        var eventA = Event("mock-cin-cle", "CLE", "CIN", week1Kickoff);
        var eventB = Event("mock-phi-dal", "DAL", "PHI", week1Kickoff.AddHours(3));
        var eventC = Event("mock-buf-nyj", "NYJ", "BUF", week2Kickoff);

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

    private static FootballEvent Event(string id, string home, string away, DateTimeOffset kickoff) =>
        new()
        {
            EventId = id,
            HomeTeam = home,
            AwayTeam = away,
            CommenceTime = kickoff,
            PhaseHint = NflSeasonPhase.Preseason,
            Phase = NflSeasonPhase.Preseason
        };

    private static DateTimeOffset NextThursday(DateTimeOffset now)
    {
        var date = now.UtcDateTime.Date;
        var daysUntil = ((int)DayOfWeek.Thursday - (int)date.DayOfWeek + 7) % 7;
        if (daysUntil == 0 && now.UtcDateTime.Hour >= 20)
        {
            daysUntil = 7;
        }

        return new DateTimeOffset(date.AddDays(daysUntil), TimeSpan.Zero);
    }
}
