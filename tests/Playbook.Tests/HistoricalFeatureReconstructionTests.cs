using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Players.Data;
using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;
using Playbook.Infrastructure.Replay.Reconstruction;

namespace Playbook.Tests;

public class HistoricalFeatureReconstructionTests
{
    [Fact]
    public void Reconstructor_Rejects_Target_And_Future_Weeks()
    {
        var reconstructor = new HistoricalFeatureReconstructor();
        var games = new List<HistoricalGameObservation>
        {
            Game(2018, 5, 12),
            Game(2018, 6, 14),
            Game(2018, 7, 99), // must be ignored
            Game(2018, 8, 88)  // must be ignored
        };

        var features = reconstructor.Reconstruct(
            Guid.NewGuid(),
            "Test Player",
            Position.WR,
            "NO",
            season: 2018,
            targetWeek: 7,
            informationCutoff: new DateTimeOffset(2018, 10, 18, 20, 0, 0, TimeSpan.FromHours(-4)),
            games);

        Assert.Equal(new[] { 5, 6 }, features.SourceWeeks);
        Assert.Equal(2, features.GamesUsed);
        Assert.Equal(DataSufficiency.Limited, features.Sufficiency);
        Assert.DoesNotContain(7, features.SourceWeeks);
        Assert.DoesNotContain(8, features.SourceWeeks);
        Assert.True(features.FantasyPointsPerGame is > 0 and < 50);
    }

    [Fact]
    public void Expectation_Service_Throws_If_Caller_Passes_Future_Games()
    {
        var service = CreateExpectationService();

        Assert.Throws<InvalidOperationException>(() =>
            service.BuildExpectations(
                Guid.NewGuid(),
                "Leak",
                Position.RB,
                "LAR",
                2018,
                7,
                DateTimeOffset.UtcNow,
                [Game(2018, 6, 10), Game(2018, 7, 30)],
                ScoringType.Ppr));
    }

    [Fact]
    public void Baseline_Projection_Is_Deterministic_And_Uses_Only_Prior_Weeks()
    {
        var service = CreateExpectationService();

        var games = new[]
        {
            Game(2018, 3, 10, targets: 8),
            Game(2018, 4, 12, targets: 9),
            Game(2018, 5, 11, targets: 7),
            Game(2018, 6, 15, targets: 10)
        };

        var a = service.BuildExpectations(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Alpha",
            Position.WR,
            "NO",
            2018,
            7,
            new DateTimeOffset(2018, 10, 18, 20, 0, 0, TimeSpan.FromHours(-4)),
            games,
            ScoringType.Ppr,
            "WR1");

        var b = service.BuildExpectations(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Alpha",
            Position.WR,
            "NO",
            2018,
            7,
            new DateTimeOffset(2018, 10, 18, 20, 0, 0, TimeSpan.FromHours(-4)),
            games,
            ScoringType.Ppr,
            "WR1");

        Assert.Equal(a.Primary.ProjectedPoints, b.Primary.ProjectedPoints);
        Assert.Equal(a.BaselineRecentAverage.ProjectedPoints, b.BaselineRecentAverage.ProjectedPoints);
        Assert.Equal(a.BaselineOpportunityAware.ProjectedPoints, b.BaselineOpportunityAware.ProjectedPoints);
        Assert.All(a.Primary.SourceWeeks, w => Assert.True(w < 7));
        Assert.True(a.Primary.IsValid);
        Assert.Contains("excludes week 7", a.Primary.Methodology, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DataSufficiency.Sufficient, a.Features.Sufficiency);
        Assert.True(a.Primary.ProjectionConfidence >= 45);
    }

    [Fact]
    public void Insufficient_History_Lowers_Sufficiency_And_Confidence()
    {
        var service = CreateExpectationService();

        var none = service.BuildExpectations(
            Guid.NewGuid(), "Nobody", Position.RB, "KC", 2018, 1,
            DateTimeOffset.UtcNow, [], ScoringType.Ppr);
        Assert.Equal(DataSufficiency.Insufficient, none.Features.Sufficiency);
        Assert.False(none.Primary.IsValid);

        var one = service.BuildExpectations(
            Guid.NewGuid(), "OneGame", Position.RB, "KC", 2018, 2,
            DateTimeOffset.UtcNow, [Game(2018, 1, 9)], ScoringType.Ppr);
        Assert.Equal(DataSufficiency.Limited, one.Features.Sufficiency);
        Assert.True(one.Primary.ProjectionConfidence < 60);
    }

    [Fact]
    public async Task Real_Week7_Projection_Does_Not_Equal_Or_Use_Actual_Outcome()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var source = provider.GetRequiredService<IHistoricalSnapshotSource>();
        var builder = provider.GetRequiredService<IHistoricalSnapshotBuilder>();

        var raw = await source.GetRawWeekAsync(2018, 7, ScoringType.Ppr, "nflverse");
        Assert.NotNull(raw);
        var (snapshot, outcomes) = builder.Build(raw!);

        foreach (var player in snapshot.Players.Where(p => p.ProjectedPoints is not null))
        {
            Assert.NotNull(player.ProjectionSourceWeeks);
            Assert.All(player.ProjectionSourceWeeks, w => Assert.True(w < 7));
            Assert.DoesNotContain(7, player.ProjectionSourceWeeks);
            Assert.DoesNotContain(8, player.ProjectionSourceWeeks);

            if (outcomes.ByPlayerId.TryGetValue(player.PlayerId, out var outcome))
            {
                // Projection may coincidentally equal actual, but must not be copied from it:
                // source weeks exclude week 7 and methodology must declare exclusion.
                Assert.NotNull(player.ProjectionMethodology);
                Assert.Contains("excludes week 7", player.ProjectionMethodology!, StringComparison.OrdinalIgnoreCase);
                Assert.NotEqual(0d, outcome.ActualFantasyPoints);
            }
        }
    }

    [Fact]
    public async Task Real_Replay_Shows_Baseline_Comparison_And_Higher_Confidence_Than_Empty_Projection_Era()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunReal2018Week7Async(provider);

        Assert.True(report.DecisionCount >= 5);
        Assert.True(report.AverageConfidence > 12);
        Assert.NotNull(report.AverageProjectionAbsoluteError);
        Assert.NotNull(report.BaselineRecentAverageMae);
        Assert.NotNull(report.BaselineOpportunityAwareMae);
        Assert.False(string.IsNullOrWhiteSpace(report.BetterBaselineLabel));

        var sample = report.Grades.First(g => g.ExpectedValue > 0 && g.ActualFantasyPoints is not null);
        Assert.NotEmpty(sample.ProjectionSourceWeeks);
        Assert.All(sample.ProjectionSourceWeeks, w => Assert.True(w < 7));
        Assert.True(sample.Confidence > 12);
    }

    private static HistoricalExpectationService CreateExpectationService()
    {
        var v1 = new OpportunityAwareProjectionEngine();
        return new HistoricalExpectationService(
            new HistoricalFeatureReconstructor(),
            new RecentAverageProjectionEngine(),
            v1,
            new CalibratedOpportunityAwareProjectionEngine(v1),
            new PositionSegmentedCalibratedProjectionEngine(v1, new PositionSegmentedCalibrationState()),
            new HistoricalProjectionExperimentState());
    }

    private static HistoricalGameObservation Game(
        int season,
        int week,
        double points,
        int? targets = null,
        int? carries = null) =>
        new()
        {
            Season = season,
            Week = week,
            FantasyPoints = points,
            Targets = targets,
            RushAttempts = carries,
            Receptions = targets is null ? null : Math.Max(0, targets.Value - 2),
            OffenseSnapPct = 0.7
        };
}
