using Playbook.Core.Predictions.Models;
using Xunit;
using Xunit.Abstractions;

namespace Playbook.Tests;

/// <summary>
/// Chronological backtest of the QB-quality feature using the SHIPPED implementation —
/// TeamPointsFeatureBuilder, QuarterbackFormBuilder and TeamPointsModel exactly as production
/// calls them, over committed real NFL data.
///
/// This exists because a model claim verified only in a throwaway analysis script is not
/// verified. Running the real code against real outcomes is the only way the reported gain can
/// be trusted, and it keeps the claim reproducible in CI.
///
/// Baseline and enhanced are evaluated on IDENTICAL samples, so the delta is apples-to-apples
/// even though absolute error depends on how much history the fixture carries.
/// </summary>
public class QuarterbackBacktestTests(ITestOutputHelper output)
{
    private sealed record Result(int Samples, decimal BaselineMae, decimal EnhancedMae)
    {
        public decimal Delta => EnhancedMae - BaselineMae;
        public decimal PercentChange => BaselineMae == 0 ? 0 : Delta / BaselineMae * 100m;
    }

    [Fact]
    public void QbFeature_ImprovesAccuracy_OnHeldOutSeasons_UsingShippedCode()
    {
        var scores = RealScoreFixture.LoadAll();
        var qbLines = RealQuarterbackFixture.LoadAll();

        Assert.True(scores.Count >= 500, $"score fixture missing (found {scores.Count})");
        Assert.True(qbLines.Count >= 1000, $"QB fixture missing (found {qbLines.Count})");

        // 2025 is the fully held-out test season and the only one whose prior season is present
        // in the committed fixture, so it is the cleanest comparison available offline.
        var test = Evaluate(2025, scores, qbLines);
        var validation = Evaluate(2024, scores, qbLines);

        output.WriteLine($"2024 n={validation.Samples} baseline={validation.BaselineMae:0.0000} "
                         + $"enhanced={validation.EnhancedMae:0.0000} ({validation.PercentChange:+0.00;-0.00}%)");
        output.WriteLine($"2025 n={test.Samples} baseline={test.BaselineMae:0.0000} "
                         + $"enhanced={test.EnhancedMae:0.0000} ({test.PercentChange:+0.00;-0.00}%)");

        Assert.True(test.Samples > 200, $"expected a full test season, got {test.Samples}");

        // The claim being pinned: QB quality reduces error on the held-out test season.
        Assert.True(
            test.EnhancedMae < test.BaselineMae,
            $"2025 enhanced MAE {test.EnhancedMae:0.0000} must beat baseline {test.BaselineMae:0.0000}");

        // Errors must stay in NFL-points territory, not fantasy magnitudes.
        Assert.InRange(test.EnhancedMae, 5m, 10m);
    }

    [Fact]
    public void QbFeature_ActuallyEngages_OnMostOfTheTestSeason()
    {
        // Guards against a silently inert feature: if QB evidence were rarely found, the
        // "improvement" above would be measuring almost nothing.
        var scores = RealScoreFixture.Load(2025);
        var qbLines = RealQuarterbackFixture.Load(2025);

        var withQb = 0;
        var total = 0;

        foreach (var (team, opponent, isHome, game) in TeamGames(scores))
        {
            var features = TeamPointsFeatureBuilder.Build(
                team, opponent, isHome, game.Season, game.Week, scores);
            if (features is null || TeamPointsModel.Predict(features) is null)
            {
                continue;
            }

            total++;
            if (QuarterbackFormBuilder.Build(team, game.Season, game.Week, qbLines) is not null)
            {
                withQb++;
            }
        }

        output.WriteLine($"2025: {withQb}/{total} predictions carried QB evidence");
        Assert.True(total > 200);
        Assert.True(withQb > total * 0.9, $"QB evidence should cover almost every game, got {withQb}/{total}");
    }

    private static Result Evaluate(
        int season,
        IReadOnlyList<HistoricalGameScore> allScores,
        IReadOnlyList<QuarterbackGameLine> allQbLines)
    {
        var seasonScores = allScores.Where(g => g.Season == season || g.Season == season - 1).ToList();
        var qbLines = allQbLines.Where(l => l.Season == season).ToList();

        decimal baselineError = 0, enhancedError = 0;
        var n = 0;

        foreach (var (team, opponent, isHome, game) in
                 TeamGames(allScores.Where(g => g.Season == season).ToList()))
        {
            var baseFeatures = TeamPointsFeatureBuilder.Build(
                team, opponent, isHome, game.Season, game.Week, seasonScores);
            if (baseFeatures is null)
            {
                continue;
            }

            var qbForm = QuarterbackFormBuilder.Build(team, game.Season, game.Week, qbLines);
            var enhancedFeatures = qbForm is null
                ? baseFeatures
                : baseFeatures with { QuarterbackEpaPerAttempt = qbForm.EpaPerAttempt };

            var baseline = TeamPointsModel.Predict(baseFeatures);
            var enhanced = TeamPointsModel.Predict(enhancedFeatures);
            if (baseline is null || enhanced is null)
            {
                continue;
            }

            var actual = (decimal)game.PointsFor(team);
            baselineError += Math.Abs(baseline.ExpectedPoints - actual);
            enhancedError += Math.Abs(enhanced.ExpectedPoints - actual);
            n++;
        }

        return n == 0
            ? new Result(0, 0, 0)
            : new Result(n, baselineError / n, enhancedError / n);
    }

    private static IEnumerable<(string Team, string Opponent, bool IsHome, HistoricalGameScore Game)>
        TeamGames(IReadOnlyList<HistoricalGameScore> games)
    {
        foreach (var game in games.OrderBy(g => g.Week))
        {
            yield return (game.HomeTeam, game.AwayTeam, true, game);
            yield return (game.AwayTeam, game.HomeTeam, false, game);
        }
    }
}
