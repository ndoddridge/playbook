using Playbook.Application.Players;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Players;
using Playbook.Core.Predictions;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Deterministic mock NFL prop lines for local development without an Odds API key.
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
        var kickoff = now.Date.AddDays(1).AddHours(20); // tomorrow 8pm UTC-ish placeholder
        var eventA = new FootballEvent
        {
            EventId = "mock-cin-cle",
            HomeTeam = "CLE",
            AwayTeam = "CIN",
            CommenceTime = new DateTimeOffset(kickoff, TimeSpan.Zero)
        };
        var eventB = new FootballEvent
        {
            EventId = "mock-phi-dal",
            HomeTeam = "DAL",
            AwayTeam = "PHI",
            CommenceTime = new DateTimeOffset(kickoff.AddHours(3), TimeSpan.Zero)
        };

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
            // Intentionally stale line for freshness handling tests / UI distinction.
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
                TeamName = null,
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
                Line = null,
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
            }
        };

        // Missing player intelligence / unknown player line (still valid market row).
        lines.Add(new PropLine
        {
            Id = "mock-unknown-rec-yds",
            Event = eventA,
            PlayerId = null,
            PlayerName = "Unknown Receiver",
            TeamName = "CIN",
            Market = PredictionMarketType.ReceivingYards,
            Line = 55.5m,
            Bookmaker = "MockBook",
            Source = "Mock",
            UpdatedAt = now,
            Freshness = PropLineFreshness.Mock
        });

        return Task.FromResult<IReadOnlyList<PropLine>>(lines);
    }
}
