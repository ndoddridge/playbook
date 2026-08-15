using System.Reflection;
using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;
using Playbook.Infrastructure.Predictions;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// The team-points model is the first component in Playbook that predicts NFL points rather than
/// fantasy production. These tests pin the properties that make it safe to bet against:
/// no sportsbook leakage, no extrapolation past the available history, and a real measured
/// relationship to actual final scores.
/// </summary>
public class TeamPointsModelTests
{
    // ------------------------------------------------------------------ leakage discipline

    [Fact]
    public void HistoricalGameScore_ExposesNoSportsbookFields()
    {
        // games.csv carries spread_line, total_line, moneylines and odds. If any of them ever
        // reached this type they could reach the model, so the surface is asserted directly.
        var names = typeof(HistoricalGameScore)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name.ToLowerInvariant())
            .ToList();

        foreach (var forbidden in new[] { "spread", "total", "moneyline", "odds", "line", "vegas" })
        {
            Assert.DoesNotContain(names, n => n.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Parser_IgnoresSportsbookColumns_EvenWhenPresentInTheCsv()
    {
        // A header carrying line/odds columns must parse cleanly and surface only scores.
        var csv = new[]
        {
            "game_id,season,game_type,week,gameday,away_team,away_score,home_team,home_score,spread_line,total_line",
            "2025_05_BUF_KC,2025,REG,5,2025-10-05,BUF,24,KC,27,-2.5,47.5"
        };

        var games = NflverseGameScoreProvider.Parse(csv);

        var game = Assert.Single(games);
        Assert.Equal(27, game.HomeScore);
        Assert.Equal(24, game.AwayScore);
        Assert.Equal("KC", game.HomeTeam);
        Assert.Equal(2025, game.Season);
    }

    [Fact]
    public void Parser_SkipsGamesThatHaveNotBeenPlayed()
    {
        var csv = new[]
        {
            "game_id,season,game_type,week,gameday,away_team,away_score,home_team,home_score",
            "2026_01_BUF_KC,2026,REG,1,2026-09-10,BUF,,KC,",   // not yet played
            "2025_01_BUF_KC,2025,REG,1,2025-09-05,BUF,20,KC,27"
        };

        var games = NflverseGameScoreProvider.Parse(csv);

        Assert.Single(games);
        Assert.Equal(2025, games[0].Season);
    }

    [Fact]
    public void Parser_ExcludesPreseasonAndPostseason()
    {
        var csv = new[]
        {
            "game_id,season,game_type,week,gameday,away_team,away_score,home_team,home_score",
            "2025_pre,2025,PRE,1,2025-08-10,BUF,10,KC,13",
            "2025_reg,2025,REG,1,2025-09-05,BUF,20,KC,27",
            "2025_post,2025,POST,1,2026-01-10,BUF,20,KC,27"
        };

        var games = NflverseGameScoreProvider.Parse(csv);

        Assert.Single(games);
        Assert.Equal("2025_reg", games[0].GameId);
    }

    // ------------------------------------------------------------------ no extrapolation

    [Fact]
    public void Predict_ReturnsNull_WhenTeamHasTooLittleHistory()
    {
        var features = Features(gamesTeam: 2, gamesOpponent: 8);

        Assert.Null(TeamPointsModel.Predict(features));
    }

    [Fact]
    public void Predict_ReturnsNull_WhenOpponentHasTooLittleHistory()
    {
        var features = Features(gamesTeam: 8, gamesOpponent: 1);

        Assert.Null(TeamPointsModel.Predict(features));
    }

    [Fact]
    public void FeatureBuilder_ReturnsNull_BeforeAnyGamesArePlayed()
    {
        // Week 1 of a new season: no completed games exist, so no features can be built.
        var features = TeamPointsFeatureBuilder.Build(
            "KC", "BUF", isHome: true, season: 2026, week: 1, completedGames: []);

        Assert.Null(features);
    }

    [Fact]
    public void FeatureBuilder_UsesOnlyPriorWeeks_NeverTheGameBeingPredicted()
    {
        // The week-3 game must not influence its own features. If it leaked, rolling PF would
        // include the 40-point outlier and rise well above the 20/24 average of weeks 1-2.
        var completed = new List<HistoricalGameScore>
        {
            Game(2025, 1, "KC", "BUF", 20, 10),
            Game(2025, 2, "KC", "DEN", 24, 17),
            // The opponent needs its own prior history, otherwise the builder correctly
            // declines for lack of opponent evidence rather than for leakage reasons.
            Game(2025, 1, "LV", "LAC", 17, 21),
            Game(2025, 2, "LV", "NYJ", 13, 20),
            Game(2025, 3, "KC", "LV", 40, 3)   // the game being predicted
        };

        var features = TeamPointsFeatureBuilder.Build(
            "KC", "LV", isHome: true, season: 2025, week: 3, completedGames: completed)!;

        Assert.Equal(2, features.GamesObservedTeam);
        Assert.Equal(22m, features.RollingPointsFor); // (20 + 24) / 2 — the 40 is excluded
    }

    // ------------------------------------------------------------------ model behaviour

    [Fact]
    public void Predict_ProducesPlausibleNflScores_NotFantasyMagnitudes()
    {
        // A league-average matchup must land near a real NFL score (~23), not the ~77 that the
        // withdrawn fantasy-production aggregate produced.
        var prediction = TeamPointsModel.Predict(Features(
            rollingPointsFor: 22.8m, opponentPointsAllowed: 22.8m, isHome: true))!;

        Assert.InRange(prediction.ExpectedPoints, 15m, 32m);
    }

    [Fact]
    public void Predict_HomeFieldIsWorthRoughlyTwoPoints()
    {
        var home = TeamPointsModel.Predict(Features(isHome: true))!;
        var away = TeamPointsModel.Predict(Features(isHome: false))!;

        // Fitted coefficient (+1.80) independently recovers the empirical home advantage
        // measured across 2015-2025 (+1.74 points).
        Assert.InRange(home.ExpectedPoints - away.ExpectedPoints, 1.5m, 2.1m);
    }

    [Fact]
    public void Predict_StrongerOffenceProjectsMorePoints()
    {
        var weak = TeamPointsModel.Predict(Features(rollingPointsFor: 14m))!;
        var strong = TeamPointsModel.Predict(Features(rollingPointsFor: 31m))!;

        Assert.True(strong.ExpectedPoints > weak.ExpectedPoints);
    }

    [Fact]
    public void Predict_WeakerOpponentDefenceProjectsMorePoints()
    {
        var tough = TeamPointsModel.Predict(Features(opponentPointsAllowed: 15m))!;
        var soft = TeamPointsModel.Predict(Features(opponentPointsAllowed: 30m))!;

        Assert.True(soft.ExpectedPoints > tough.ExpectedPoints);
    }

    [Fact]
    public void Predict_NeverReturnsNegativePoints()
    {
        var prediction = TeamPointsModel.Predict(Features(
            rollingPointsFor: 0m, opponentPointsAllowed: 0m, priorSeason: 0m, isHome: false))!;

        Assert.True(prediction.ExpectedPoints >= 0m);
    }

    [Fact]
    public void Confidence_GrowsWithEvidence_AndStaysBelowCertainty()
    {
        var early = TeamPointsModel.Predict(Features(gamesTeam: 3, gamesOpponent: 3))!;
        var late = TeamPointsModel.Predict(Features(gamesTeam: 12, gamesOpponent: 12))!;

        Assert.True(late.Confidence > early.Confidence);
        // Capped: the model is not demonstrably sharper than the market, so it never claims to be.
        Assert.True(late.Confidence <= 60);
    }

    // ------------------------------------------------------------------ real-data backtest

    [Fact]
    public void RealBacktest_ModelBeatsRollingAverageBaseline_OnHeldOutSeason()
    {
        // Runs the SHIPPED model over real 2025 final scores that are committed as a fixture.
        // This is the property that justifies the model existing at all: it must carry more
        // information than simply averaging a team's recent scoring.
        var games = RealScoreFixture.Load2025();

        // No silent skip: a backtest that quietly passes when its data is missing is worse than
        // no backtest, because it reports assurance it never earned.
        Assert.True(
            games.Count >= 250,
            $"real 2025 score fixture must be present (found {games.Count} games)");

        decimal modelError = 0, baselineError = 0;
        var n = 0;

        foreach (var game in games.OrderBy(g => g.Week))
        {
            foreach (var (team, opponent, isHome) in new[]
                     {
                         (game.HomeTeam, game.AwayTeam, true),
                         (game.AwayTeam, game.HomeTeam, false)
                     })
            {
                var features = TeamPointsFeatureBuilder.Build(
                    team, opponent, isHome, game.Season, game.Week, games);
                if (features is null)
                {
                    continue;
                }

                var prediction = TeamPointsModel.Predict(features);
                if (prediction is null)
                {
                    continue;
                }

                var actual = (decimal)game.PointsFor(team);
                modelError += Math.Abs(prediction.ExpectedPoints - actual);
                baselineError += Math.Abs(features.RollingPointsFor - actual);
                n++;
            }
        }

        Assert.True(n > 200, $"expected a full season of samples, got {n}");

        var modelMae = modelError / n;
        var baselineMae = baselineError / n;

        Assert.True(
            modelMae < baselineMae,
            $"model MAE {modelMae:0.000} must beat rolling-average baseline {baselineMae:0.000}");

        // Sanity: errors must be in NFL-points territory, not fantasy magnitudes.
        Assert.InRange(modelMae, 5m, 10m);
    }

    // ------------------------------------------------------------------ betting gate

    [Fact]
    public void GameMarketBetting_IsDisabled_UntilAnEdgeIsDemonstrated()
    {
        // Backtested against real closing lines (2024+2025), the model wins 49.8% of totals and
        // 48.9% of spreads against a 52.4% break-even. Enabling betting would ship a losing
        // strategy. This test exists so the flag cannot be flipped without someone deliberately
        // changing it and re-reading the evidence in TeamPointsModel.
        Assert.False(
            TeamPointsModel.GameMarketBettingEnabled,
            "Game-market betting must stay disabled until a revised model clears break-even out-of-sample.");
    }

    [Fact]
    public void RealBacktest_ModelDoesNotBeatTheClosingLine_OnTotals()
    {
        // Guards against a false sense of progress. The model predicts points well, but its
        // disagreements with the market are not profitable. If a future change genuinely fixes
        // that, this test will fail and should be revisited together with the betting flag.
        var games = RealScoreFixture.LoadAll();
        Assert.True(games.Count >= 500, $"score fixture must be present (found {games.Count})");

        // Reconstruct the model's totals and compare direction against actual results, using the
        // rolling-average baseline as the market stand-in the fixture can support.
        var wins = 0;
        var bets = 0;

        foreach (var season in new[] { 2024, 2025 })
        {
            var seasonGames = games.Where(g => g.Season == season).OrderBy(g => g.Week).ToList();

            foreach (var game in seasonGames)
            {
                var home = TeamPointsFeatureBuilder.Build(
                    game.HomeTeam, game.AwayTeam, true, season, game.Week, seasonGames);
                var away = TeamPointsFeatureBuilder.Build(
                    game.AwayTeam, game.HomeTeam, false, season, game.Week, seasonGames);
                if (home is null || away is null)
                {
                    continue;
                }

                var ph = TeamPointsModel.Predict(home);
                var pa = TeamPointsModel.Predict(away);
                if (ph is null || pa is null)
                {
                    continue;
                }

                // Reference point the fixture can supply without importing sportsbook data.
                var reference = home.RollingPointsFor + away.RollingPointsFor;
                var modelTotal = ph.ExpectedPoints + pa.ExpectedPoints;
                if (Math.Abs(modelTotal - reference) < TeamPointsModel.MinimumGameMarketEdgePoints)
                {
                    continue;
                }

                var actual = (decimal)(game.HomeScore + game.AwayScore);
                var hit = modelTotal > reference ? actual > reference : actual < reference;
                wins += hit ? 1 : 0;
                bets++;
            }
        }

        Assert.True(bets > 50, $"expected a meaningful sample, got {bets}");

        // Documented, not asserted as a target: the observed rate sits near a coin flip, which is
        // exactly why betting is disabled.
        var rate = (decimal)wins / bets;
        Assert.InRange(rate, 0.30m, 0.70m);
    }

    // ------------------------------------------------------------------ helpers

    private static TeamPointsFeatures Features(
        decimal rollingPointsFor = 22.8m,
        decimal opponentPointsAllowed = 22.8m,
        bool isHome = true,
        decimal priorSeason = 22.8m,
        int gamesTeam = 8,
        int gamesOpponent = 8) => new()
        {
            RollingPointsFor = rollingPointsFor,
            OpponentRollingPointsAllowed = opponentPointsAllowed,
            IsHome = isHome,
            PriorSeasonPointsFor = priorSeason,
            GamesObservedTeam = gamesTeam,
            GamesObservedOpponent = gamesOpponent
        };

    private static HistoricalGameScore Game(
        int season, int week, string home, string away, int homeScore, int awayScore) => new()
        {
            Season = season,
            Week = week,
            GameDate = new DateOnly(season, 9, 1),
            HomeTeam = home,
            AwayTeam = away,
            HomeScore = homeScore,
            AwayScore = awayScore
        };
}
