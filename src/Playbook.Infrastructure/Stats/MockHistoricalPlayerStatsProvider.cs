using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Deterministic historical season + game-log rows for mock catalog players.
/// </summary>
public sealed class MockHistoricalPlayerStatsProvider : IHistoricalPlayerStatsProvider
{
    public HistoricalPlayerStatsProviderKind Kind => HistoricalPlayerStatsProviderKind.Mock;

    public string DisplayName => "Mock (historical)";

    public bool IsConfigured => true;

    public Task<HistoricalPlayerStatsBatch> GetHistoricalStatsAsync(
        HistoricalPlayerStatsSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var mahomes = Guid.Parse("11111111-1111-1111-1111-111111111103");
        var cmc = Guid.Parse("11111111-1111-1111-1111-111111111105");

        var games = new List<PlayerGameStats>();
        var seasons = new List<PlayerSeasonStats>();

        foreach (var season in request.Seasons)
        {
            for (var week = 1; week <= 3; week++)
            {
                games.Add(new PlayerGameStats
                {
                    PlayerId = mahomes,
                    Season = season,
                    Week = week,
                    SeasonType = "REG",
                    Level = FootballLevel.Nfl,
                    OpponentTeam = "DEN",
                    Team = "KC",
                    Position = "QB",
                    PassAttempts = 30 + week,
                    PassCompletions = 20 + week,
                    PassYards = 250 + week * 10,
                    PassTouchdowns = 2,
                    PassInterceptions = week == 2 ? 1 : 0,
                    RushAttempts = 3,
                    RushYards = 15,
                    RushTouchdowns = 0,
                    Targets = 0,
                    Receptions = 0,
                    ReceivingYards = 0,
                    ReceivingTouchdowns = 0,
                    Fumbles = 0,
                    SourceProvider = "Mock",
                    Source = "mock-game-log",
                    IdentityMatch = StatsIdentityMatch.Matched,
                    MissingFields = [],
                    LastUpdated = now
                });

                games.Add(new PlayerGameStats
                {
                    PlayerId = cmc,
                    Season = season,
                    Week = week,
                    SeasonType = "REG",
                    Level = FootballLevel.Nfl,
                    OpponentTeam = "SEA",
                    Team = "SF",
                    Position = "RB",
                    PassAttempts = 0,
                    PassCompletions = 0,
                    PassYards = 0,
                    PassTouchdowns = 0,
                    PassInterceptions = 0,
                    RushAttempts = 18 + week,
                    RushYards = 70 + week * 5,
                    RushTouchdowns = week == 1 ? 1 : 0,
                    Targets = 5,
                    Receptions = 4,
                    ReceivingYards = 30,
                    ReceivingTouchdowns = 0,
                    Fumbles = 0,
                    SourceProvider = "Mock",
                    Source = "mock-game-log",
                    IdentityMatch = StatsIdentityMatch.Matched,
                    MissingFields = [],
                    LastUpdated = now
                });
            }
        }

        // Season aggregates are produced by the primary mock season provider; this mock
        // focuses on game-log retention for pipeline tests.
        return Task.FromResult(new HistoricalPlayerStatsBatch
        {
            SeasonRecords = seasons,
            GameLogs = games,
            IdentityMatches = 2,
            UnresolvedPlayers = 0,
            ResponseTime = TimeSpan.FromMilliseconds(1)
        });
    }
}
