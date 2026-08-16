using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;
using Playbook.Infrastructure.Predictions;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// Game-market analysis exists so Playbook is not blind while the betting gate is closed.
/// The tests below protect the line between "here is what the model thinks" and "here is a
/// wager" — the whole reason this surface is allowed to exist at all.
/// </summary>
public class GameMarketAnalysisTests
{
    // ---------------------------------------------------------------- the core invariant

    [Fact]
    public void BettingDisabled_CanNeverProduceABetLabel()
    {
        // The single rule this feature must never break. Exhaustive over both projection states.
        foreach (var hasProjection in new[] { true, false })
        {
            var status = GameMarketAnalysisPolicy.ResolveStatus(hasProjection, bettingEnabled: false);
            var label = GameMarketAnalysisPolicy.StatusLabel(status);

            Assert.NotEqual(GameMarketAnalysisStatus.BetEligible, status);
            Assert.DoesNotContain("BET", label, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void LiveGate_IsClosed_SoNoAnalysisCardCanBeBetEligible()
    {
        // Wired to the real shipped constant rather than a hardcoded false, so this follows the
        // product if the gate is ever opened deliberately.
        var status = GameMarketAnalysisPolicy.ResolveStatus(
            hasProjection: true, TeamPointsModel.GameMarketBettingEnabled);

        if (!TeamPointsModel.GameMarketBettingEnabled)
        {
            Assert.Equal(GameMarketAnalysisStatus.AnalysisOnly, status);
            Assert.Equal("ANALYSIS ONLY", GameMarketAnalysisPolicy.StatusLabel(status));
        }
    }

    [Fact]
    public void MissingProjection_IsAlwaysNoPlay_EvenIfBettingWereEnabled()
    {
        var status = GameMarketAnalysisPolicy.ResolveStatus(hasProjection: false, bettingEnabled: true);

        Assert.Equal(GameMarketAnalysisStatus.NoPlay, status);
        Assert.Equal("NO PLAY", GameMarketAnalysisPolicy.StatusLabel(status));
    }

    // ---------------------------------------------------------------- real lines still render

    [Fact]
    public void RealGameLines_WithBettingDisabled_StillProduceAnalysisCards()
    {
        // The point of the milestone: 0 approved bets must not mean an empty page.
        var lines = PreseasonSlate();

        var analyses = GameMarketAnalysisBuilder.Build(
            lines, new UnavailableProjections(), bettingEnabled: false);

        Assert.Equal(2, analyses.Count);
        Assert.All(analyses, a => Assert.Equal(GameMarketAnalysisStatus.NoPlay, a.Status));

        // The real market numbers survive even though Playbook has no view.
        var buf = analyses.Single(a => a.HomeTeam == "BUF");
        Assert.Equal(-5.5m, buf.SpreadLine);
        Assert.Equal(37.5m, buf.TotalLine);
        Assert.Equal("FanDuel", buf.Bookmaker);
    }

    [Fact]
    public void Preseason_ProducesNoPlay_WithAnExplicitPreseasonReason()
    {
        var lines = PreseasonSlate();

        var analyses = GameMarketAnalysisBuilder.Build(
            lines, new UnavailableProjections("Team projections unavailable during preseason "
                                              + "(roster/lineup uncertainty too high)."),
            bettingEnabled: false);

        var game = analyses.First();
        Assert.Equal(GameMarketAnalysisStatus.NoPlay, game.Status);
        Assert.Contains("preseason", game.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(game.HasProjection);
        Assert.Null(game.ProjectedHomeMargin);
        Assert.Null(game.ProjectedTotal);
    }

    [Fact]
    public void MissingProjection_LeavesNumbersNull_RatherThanFabricatingThem()
    {
        var analyses = GameMarketAnalysisBuilder.Build(
            PreseasonSlate(), new UnavailableProjections(), bettingEnabled: false);

        Assert.All(analyses, a =>
        {
            Assert.Null(a.ProjectedHomeMargin);
            Assert.Null(a.ProjectedTotal);
            Assert.Null(a.MarginDisagreement);
            Assert.Null(a.TotalDisagreement);
        });
    }

    // ---------------------------------------------------------------- projection available

    [Fact]
    public void ProjectionAvailable_BettingDisabled_IsAnalysisOnly_WithDisagreement()
    {
        // The regular-season shape: a real projection, shown as insight rather than a wager.
        var lines = RegularSeasonSlate();

        var analyses = GameMarketAnalysisBuilder.Build(
            lines, new FixedProjections(homePoints: 27m, awayPoints: 20m), bettingEnabled: false);

        var game = Assert.Single(analyses);
        Assert.Equal(GameMarketAnalysisStatus.AnalysisOnly, game.Status);
        Assert.Equal("ANALYSIS ONLY", GameMarketAnalysisPolicy.StatusLabel(game.Status));

        Assert.Equal(7.0m, game.ProjectedHomeMargin);   // 27 - 20
        Assert.Equal(47.0m, game.ProjectedTotal);       // 27 + 20

        // Market has home -3.5, i.e. an implied home margin of +3.5. Model says +7.0.
        Assert.Equal(3.5m, game.MarginDisagreement);
        Assert.Equal(2.0m, game.TotalDisagreement);     // 47.0 vs 45.0

        // The reason states betting is DISABLED, which is the opposite of a recommendation. The
        // rule that matters — no BET label on the card — is enforced on StatusLabel and covered
        // exhaustively by BettingDisabled_CanNeverProduceABetLabel.
        Assert.Contains("disabled", game.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "BET", GameMarketAnalysisPolicy.StatusLabel(game.Status), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarginDisagreement_RespectsTheBookSignConvention()
    {
        // home -7.5 means the market expects home to win by 7.5. A model margin of +7.0 is a
        // 0.5-point disagreement, not 14.5.
        var analysis = new GameMarketAnalysis
        {
            AwayTeam = "MIA",
            HomeTeam = "BUF",
            CommenceTime = DateTimeOffset.UtcNow,
            SpreadLine = -7.5m,
            ProjectedHomeMargin = 7.0m,
            Status = GameMarketAnalysisStatus.AnalysisOnly,
            Reason = "test"
        };

        Assert.Equal(-0.5m, analysis.MarginDisagreement);
    }

    [Fact]
    public void BettingEnabled_BehaviourIsUnchanged_AndReachesBetEligible()
    {
        // Proves the enabled path still exists and is correct, without touching the live gate.
        var analyses = GameMarketAnalysisBuilder.Build(
            RegularSeasonSlate(), new FixedProjections(27m, 20m), bettingEnabled: true);

        var game = Assert.Single(analyses);
        Assert.Equal(GameMarketAnalysisStatus.BetEligible, game.Status);
    }

    [Fact]
    public void PlayerPropLines_AreNotTurnedIntoGameAnalysis()
    {
        var gameEvent = Event("BUF", "MIA", NflSeasonPhase.RegularSeason);
        var lines = new List<PropLine>
        {
            Line(gameEvent, PredictionMarketType.PassingYards, 250m),
            Line(gameEvent, PredictionMarketType.Receptions, 5.5m)
        };

        var analyses = GameMarketAnalysisBuilder.Build(
            lines, new FixedProjections(27m, 20m), bettingEnabled: false);

        Assert.Empty(analyses);
    }

    // ---------------------------------------------------------------- fixtures

    private static FootballEvent Event(string home, string away, NflSeasonPhase phase, int week = 1) => new()
    {
        EventId = $"{away}-{home}-{week}",
        HomeTeam = home,
        AwayTeam = away,
        CommenceTime = DateTimeOffset.UtcNow.AddDays(3),
        Phase = phase,
        Season = 2026,
        Week = week
    };

    private static PropLine Line(
        FootballEvent gameEvent, PredictionMarketType market, decimal line, string book = "FanDuel") => new()
        {
            Id = $"{gameEvent.EventId}:{market}",
            Event = gameEvent,
            Market = market,
            Line = line,
            Bookmaker = book,
            Source = "TheOddsAPI",
            UpdatedAt = DateTimeOffset.UtcNow,
            Freshness = PropLineFreshness.Live
        };

    /// <summary>Two real preseason games mirroring the observed 2026-08-15 slate.</summary>
    private static List<PropLine> PreseasonSlate()
    {
        var buf = Event("BUF", "CAR", NflSeasonPhase.Preseason);
        var bal = Event("BAL", "PHI", NflSeasonPhase.Preseason, week: 2);

        return
        [
            Line(buf, PredictionMarketType.Spread, -5.5m),
            Line(buf, PredictionMarketType.GameTotal, 37.5m),
            Line(bal, PredictionMarketType.Spread, 3.5m),
            Line(bal, PredictionMarketType.GameTotal, 36.5m)
        ];
    }

    private static List<PropLine> RegularSeasonSlate()
    {
        var game = Event("BUF", "MIA", NflSeasonPhase.RegularSeason, week: 8);
        return
        [
            Line(game, PredictionMarketType.Spread, -3.5m),
            Line(game, PredictionMarketType.GameTotal, 45.0m)
        ];
    }

    /// <summary>Projection service that has no view, carrying a real explanation.</summary>
    private sealed class UnavailableProjections(string reason = "Model projection unavailable")
        : ITeamGameProjectionService
    {
        public TeamGameProjection GetTeamProjection(
            string teamAbbreviation, FootballEvent gameEvent, NflSeasonPhase seasonPhase) =>
            TeamGameProjection.Unavailable(reason);
    }

    /// <summary>Projection service returning fixed real-looking NFL point totals.</summary>
    private sealed class FixedProjections(decimal homePoints, decimal awayPoints)
        : ITeamGameProjectionService
    {
        public TeamGameProjection GetTeamProjection(
            string teamAbbreviation, FootballEvent gameEvent, NflSeasonPhase seasonPhase)
        {
            var isHome = string.Equals(teamAbbreviation, "BUF", StringComparison.OrdinalIgnoreCase);
            return new TeamGameProjection
            {
                TeamAbbreviation = teamAbbreviation,
                EstimatedTeamScore = isHome ? homePoints : awayPoints,
                Confidence = 55,
                Volatility = 55,
                Reasoning = "test projection"
            };
        }
    }
}
