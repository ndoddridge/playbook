using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;
using Playbook.Core.Projections.Models;
using Playbook.Infrastructure.Predictions;
using Xunit;

namespace Playbook.Tests;

public class GameMarketProjectionTests
{
    [Fact]
    public void GameMarketProjector_GameTotal_RegularSeason_ReturnsProjection()
    {
        // When both teams have projections, game total should sum them
        var gameEvent = MakeGameEvent("BUF@MIA", NflSeasonPhase.RegularSeason);
        var line = MakePropLine(PredictionMarketType.GameTotal, gameEvent);

        var homeProj = new TeamGameProjection
        {
            TeamAbbreviation = "BUF",
            EstimatedTeamScore = 25.5m,
            Confidence = 60,
            Volatility = 40,
            Reasoning = "Test"
        };

        var awayProj = new TeamGameProjection
        {
            TeamAbbreviation = "MIA",
            EstimatedTeamScore = 21.0m,
            Confidence = 58,
            Volatility = 42,
            Reasoning = "Test"
        };

        var teamProjService = new MockTeamProjectionService(homeProj, awayProj);

        // Act
        var (projection, conf, vol) = GameMarketProjector.ProjectGameMarket(
            PredictionMarketType.GameTotal, gameEvent, line, teamProjService, NflSeasonPhase.RegularSeason);

        // Assert
        Assert.NotNull(projection);
        Assert.InRange(projection.Value, 46.4m, 46.6m);
        Assert.True(conf > 0);
    }

    [Fact]
    public void GameMarketProjector_TeamTotal_RegularSeason_ReturnsProjection()
    {
        // Team total should return the single team's projection
        var gameEvent = MakeGameEvent("BUF@MIA", NflSeasonPhase.RegularSeason);
        var line = new PropLine
        {
            Id = "1",
            Event = gameEvent,
            Market = PredictionMarketType.TeamTotal,
            Line = 24.5m,
            TeamName = "BUF",
            Bookmaker = "Caesar's",
            Source = "odds-api",
            UpdatedAt = DateTimeOffset.UtcNow,
            Freshness = PropLineFreshness.Live
        };

        var homeProj = new TeamGameProjection
        {
            TeamAbbreviation = "BUF",
            EstimatedTeamScore = 25.5m,
            Confidence = 60,
            Volatility = 40,
            Reasoning = "Test"
        };

        var teamProjService = new MockTeamProjectionService(homeProj, null);

        // Act
        var (projection, conf, vol) = GameMarketProjector.ProjectGameMarket(
            PredictionMarketType.TeamTotal, gameEvent, line, teamProjService, NflSeasonPhase.RegularSeason);

        // Assert
        Assert.NotNull(projection);
        Assert.InRange(projection.Value, 25.4m, 25.6m);
        Assert.True(conf > 0);
    }

    [Fact]
    public void GameMarketProjector_Spread_RegularSeason_ReturnsProjection()
    {
        // Spread should return home score minus away score
        var gameEvent = MakeGameEvent("BUF@MIA", NflSeasonPhase.RegularSeason);
        var line = MakePropLine(PredictionMarketType.Spread, gameEvent);

        var homeProj = new TeamGameProjection
        {
            TeamAbbreviation = "BUF",
            EstimatedTeamScore = 28.0m,
            Confidence = 60,
            Volatility = 40,
            Reasoning = "Test"
        };

        var awayProj = new TeamGameProjection
        {
            TeamAbbreviation = "MIA",
            EstimatedTeamScore = 20.0m,
            Confidence = 58,
            Volatility = 42,
            Reasoning = "Test"
        };

        var teamProjService = new MockTeamProjectionService(homeProj, awayProj);

        // Act
        var (projection, conf, vol) = GameMarketProjector.ProjectGameMarket(
            PredictionMarketType.Spread, gameEvent, line, teamProjService, NflSeasonPhase.RegularSeason);

        // Assert
        Assert.NotNull(projection);
        Assert.InRange(projection.Value, 7.9m, 8.1m);
        Assert.True(conf > 0);
    }

    [Fact]
    public void GameMarketProjector_Winner_RegularSeason_ReturnsProjection()
    {
        // Moneyline should return spread projection
        var gameEvent = MakeGameEvent("BUF@MIA", NflSeasonPhase.RegularSeason);
        var line = MakePropLine(PredictionMarketType.Winner, gameEvent);

        var homeProj = new TeamGameProjection
        {
            TeamAbbreviation = "BUF",
            EstimatedTeamScore = 27.0m,
            Confidence = 62,
            Volatility = 38,
            Reasoning = "Test"
        };

        var awayProj = new TeamGameProjection
        {
            TeamAbbreviation = "MIA",
            EstimatedTeamScore = 20.0m,
            Confidence = 58,
            Volatility = 42,
            Reasoning = "Test"
        };

        var teamProjService = new MockTeamProjectionService(homeProj, awayProj);

        // Act
        var (projection, conf, vol) = GameMarketProjector.ProjectGameMarket(
            PredictionMarketType.Winner, gameEvent, line, teamProjService, NflSeasonPhase.RegularSeason);

        // Assert
        Assert.NotNull(projection);
        Assert.InRange(projection.Value, 6.9m, 7.1m);
        Assert.True(conf > 0);
    }

    [Fact]
    public void GameMarketProjector_Preseason_ReturnsNull()
    {
        // Preseason should return unavailable (null projection)
        var gameEvent = MakeGameEvent("BUF@MIA", NflSeasonPhase.Preseason);
        var line = MakePropLine(PredictionMarketType.GameTotal, gameEvent);

        var teamProjService = new MockTeamProjectionService(null, null);

        // Act
        var (projection, conf, vol) = GameMarketProjector.ProjectGameMarket(
            PredictionMarketType.GameTotal, gameEvent, line, teamProjService, NflSeasonPhase.Preseason);

        // Assert
        Assert.Null(projection);
        Assert.Equal(0, conf);
    }

    [Fact]
    public void GameMarketProjector_PlayerPropMarket_IgnoresGameMarketLogic()
    {
        // Player props should not be processed by game market projector
        var gameEvent = MakeGameEvent("BUF@MIA", NflSeasonPhase.RegularSeason);
        var line = new PropLine
        {
            Id = "1",
            Event = gameEvent,
            Market = PredictionMarketType.PassingYards,
            Line = 250,
            Bookmaker = "Caesar's",
            Source = "odds-api",
            UpdatedAt = DateTimeOffset.UtcNow,
            Freshness = PropLineFreshness.Live
        };

        var teamProjService = new MockTeamProjectionService(null, null);

        // Act
        var (projection, conf, vol) = GameMarketProjector.ProjectGameMarket(
            PredictionMarketType.PassingYards, gameEvent, line, teamProjService, NflSeasonPhase.RegularSeason);

        // Assert
        Assert.Null(projection);
        Assert.Equal(0, conf);
    }

    [Fact]
    public void GameMarketProjector_UnavailableTeamProj_ReturnsNull()
    {
        // When team projection is unavailable, game market cannot produce a projection
        var gameEvent = MakeGameEvent("BUF@MIA", NflSeasonPhase.RegularSeason);
        var line = MakePropLine(PredictionMarketType.GameTotal, gameEvent);

        var unavailableProj = TeamGameProjection.Unavailable("No data");
        var teamProjService = new MockTeamProjectionService(unavailableProj, unavailableProj);

        // Act
        var (projection, conf, vol) = GameMarketProjector.ProjectGameMarket(
            PredictionMarketType.GameTotal, gameEvent, line, teamProjService, NflSeasonPhase.RegularSeason);

        // Assert
        Assert.Null(projection);
        Assert.Equal(0, conf);
    }

    // Test helpers
    private static FootballEvent MakeGameEvent(string displayName, NflSeasonPhase phase)
    {
        var (home, away) = displayName.Contains("BUF") ? ("BUF", "MIA") : ("MIA", "BUF");
        return new FootballEvent
        {
            EventId = $"{home}-{away}-{Guid.NewGuid()}",
            HomeTeam = home,
            AwayTeam = away,
            CommenceTime = DateTimeOffset.UtcNow.AddDays(7),
            Phase = phase,
            Season = 2026,
            Week = 1
        };
    }

    private static PropLine MakePropLine(PredictionMarketType market, FootballEvent gameEvent)
    {
        return new PropLine
        {
            Id = "test-line-1",
            Event = gameEvent,
            Market = market,
            Line = 45.5m,
            Bookmaker = "Caesar's",
            Source = "odds-api",
            UpdatedAt = DateTimeOffset.UtcNow,
            Freshness = PropLineFreshness.Live
        };
    }

    [Fact]
    public void TeamGameProjectionService_ShouldNotUseSportsbookLineAsProjection()
    {
        // Verifies we do NOT use the sportsbook line as the Playbook projection.
        // The sportsbook line is the MARKET we evaluate, not our projection input.
        var gameEvent = MakeGameEvent("BUF@MIA", NflSeasonPhase.RegularSeason);
        var line = new PropLine
        {
            Id = "1",
            Event = gameEvent,
            Market = PredictionMarketType.GameTotal,
            Line = 45.5m, // This is the market line, NOT our projection
            Bookmaker = "Caesar's",
            Source = "odds-api",
            UpdatedAt = DateTimeOffset.UtcNow,
            Freshness = PropLineFreshness.Live
        };

        // Real projection from player aggregation (different from sportsbook line)
        var homeProj = new TeamGameProjection
        {
            TeamAbbreviation = "BUF",
            EstimatedTeamScore = 26.0m,
            Confidence = 65,
            Volatility = 40,
            Reasoning = "Player aggregation"
        };

        var awayProj = new TeamGameProjection
        {
            TeamAbbreviation = "MIA",
            EstimatedTeamScore = 20.5m,
            Confidence = 60,
            Volatility = 42,
            Reasoning = "Player aggregation"
        };

        var teamProjService = new MockTeamProjectionService(homeProj, awayProj);

        // Act
        var (projection, conf, vol) = GameMarketProjector.ProjectGameMarket(
            PredictionMarketType.GameTotal, gameEvent, line, teamProjService, NflSeasonPhase.RegularSeason);

        // Assert - projection should be our estimate (46.5), NOT the sportsbook line (45.5)
        Assert.NotNull(projection);
        Assert.InRange(projection.Value, 46.4m, 46.6m);
        Assert.NotEqual(45.5m, projection.Value);
    }

    /// <summary>Mock team projection service for testing game-market logic without real aggregation.</summary>
    private sealed class MockTeamProjectionService(TeamGameProjection? home, TeamGameProjection? away)
        : ITeamGameProjectionService
    {
        public TeamGameProjection GetTeamProjection(
            string teamAbbreviation, FootballEvent gameEvent, NflSeasonPhase seasonPhase)
        {
            if (seasonPhase == NflSeasonPhase.Preseason)
            {
                return TeamGameProjection.Unavailable("Preseason unavailable");
            }

            if (string.Equals(teamAbbreviation, "BUF", StringComparison.OrdinalIgnoreCase))
            {
                return home ?? TeamGameProjection.Unavailable();
            }

            return away ?? TeamGameProjection.Unavailable();
        }
    }
}
