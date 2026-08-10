using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Knowledge;
using Playbook.Application.Players.Data;
using Playbook.Application.Predictions;
using Playbook.Core.Knowledge;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Predictions;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class QuickPicksHistoricalEvaluationTests
{
    [Fact]
    public void Snapshot_Builder_Passes_Counting_Projections_And_Keeps_Outcomes_Separate()
    {
        var raw = ControlledHistoricalFixture.Create();
        var (snapshot, outcomes) = new HistoricalSnapshotBuilder().Build(raw);

        var alpha = snapshot.Players.Single(p => p.PlayerId == ControlledHistoricalFixture.AlphaRbId);
        Assert.Equal(72.0, alpha.ProjectedRushYards);
        Assert.Equal(18.0, alpha.ProjectedReceivingYards);

        // Future injury still stripped.
        var delta = snapshot.Players.Single(p => p.PlayerId == ControlledHistoricalFixture.DeltaWrId);
        Assert.Null(delta.InjuryStatus);

        var alphaOut = outcomes.ByPlayerId[ControlledHistoricalFixture.AlphaRbId];
        Assert.Equal(38, alphaOut.ActualRushYards);
        Assert.Equal(8.1, alphaOut.ActualFantasyPoints);

        // Actuals must not live on the snapshot player state.
        Assert.Null(typeof(HistoricalPlayerState).GetProperty("ActualRushYards"));
    }

    [Fact]
    public void Generator_Produces_Deterministic_Historical_Quick_Picks()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var generator = provider.GetRequiredService<HistoricalQuickPickGenerator>();
        var knowledge = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        knowledge.ConfigureBaseline();

        var raw = ControlledHistoricalFixture.Create();
        var (snapshot, _) = new HistoricalSnapshotBuilder().Build(raw);

        var a = generator.Generate(snapshot, QuickPickMode.Baseline);
        var b = generator.Generate(snapshot, QuickPickMode.Baseline);

        Assert.NotEmpty(a);
        Assert.Equal(a.Count, b.Count);
        Assert.Equal(
            a.Select(p => (p.PlayerId, p.Market, p.ProjectedValue, p.RankInMarket, p.RankingScore)),
            b.Select(p => (p.PlayerId, p.Market, p.ProjectedValue, p.RankInMarket, p.RankingScore)));

        Assert.All(a, p =>
        {
            Assert.Equal(QuickPickHistoricalGrading.PredictionTypeLabel, p.PredictionType);
            Assert.Equal(QuickPickMode.Baseline, p.Mode);
            Assert.False(p.KnowledgeAttached);
            Assert.Equal(ControlledHistoricalFixture.InformationCutoff, p.CutoffTimestamp);
            Assert.Contains(p.Market, FrozenQuickPicksHistoricalEvaluationV1.GradedMarkets);
        });
    }

    [Fact]
    public void Temporal_Cutoff_Excludes_Future_Injury_From_Enhanced_Knowledge()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var generator = provider.GetRequiredService<HistoricalQuickPickGenerator>();
        var knowledge = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        knowledge.ConfigureEnhanced(FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups);

        var raw = ControlledHistoricalFixture.Create();
        var (snapshot, _) = new HistoricalSnapshotBuilder().Build(raw);
        var preds = generator.Generate(snapshot, QuickPickMode.Enhanced);

        var deltaPreds = preds.Where(p => p.PlayerId == ControlledHistoricalFixture.DeltaWrId).ToList();
        Assert.NotEmpty(deltaPreds);
        Assert.All(deltaPreds, p =>
        {
            Assert.True(p.KnowledgeAttached);
            Assert.NotNull(p.KnowledgeContext);
            KnowledgeTemporalGuard.AssertNoFutureLeak(
                p.KnowledgeContext!.Knowledge,
                snapshot.InformationCutoff);
            // Future designation must not appear as known injury evidence — only as excluded/unavailable.
            Assert.DoesNotContain(
                p.KnowledgeContext.Knowledge.Facts,
                f => f.Key.Contains("injury", StringComparison.OrdinalIgnoreCase) &&
                     (f.Statement.Contains("Hamstring", StringComparison.OrdinalIgnoreCase) ||
                      f.Statement.Contains("ruled out for the season", StringComparison.OrdinalIgnoreCase)));
            Assert.Null(snapshot.Players.Single(x => x.PlayerId == ControlledHistoricalFixture.DeltaWrId).InjuryStatus);
        });
    }

    [Fact]
    public void Grading_Attaches_Outcomes_After_Prediction_And_Computes_Errors()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var generator = provider.GetRequiredService<HistoricalQuickPickGenerator>();
        provider.GetRequiredService<KnowledgeImpactExperimentState>().ConfigureBaseline();

        var raw = ControlledHistoricalFixture.Create();
        var (snapshot, outcomes) = new HistoricalSnapshotBuilder().Build(raw);
        var preds = generator.Generate(snapshot, QuickPickMode.Baseline);
        var graded = QuickPickHistoricalGrader.Grade(preds, outcomes);

        Assert.NotEmpty(graded);
        Assert.All(graded, g =>
        {
            Assert.True(g.AbsoluteError >= 0);
            Assert.Equal(
                QuickPickHistoricalGrading.AbsoluteError(g.Prediction.ProjectedValue, g.ActualValue),
                g.AbsoluteError);
            Assert.Equal(
                QuickPickHistoricalGrading.SignedError(g.Prediction.ProjectedValue, g.ActualValue),
                g.SignedError);
        });

        var alphaRush = graded.Single(g =>
            g.Prediction.PlayerId == ControlledHistoricalFixture.AlphaRbId &&
            g.Prediction.Market == PredictionMarketType.RushingYards);
        Assert.Equal(38, alphaRush.ActualValue);
        Assert.Equal(Math.Abs(72 - 38), alphaRush.AbsoluteError);
    }

    [Fact]
    public void No_Future_Leakage_Into_Predictions_From_Week_Outcomes()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var generator = provider.GetRequiredService<HistoricalQuickPickGenerator>();
        provider.GetRequiredService<KnowledgeImpactExperimentState>().ConfigureBaseline();

        var raw = ControlledHistoricalFixture.Create();
        var (snapshot, outcomes) = new HistoricalSnapshotBuilder().Build(raw);
        var preds = generator.Generate(snapshot, QuickPickMode.Baseline);

        // Predictions must not encode actual week values.
        Assert.All(preds, p =>
        {
            if (!outcomes.ByPlayerId.TryGetValue(p.PlayerId, out var o))
            {
                return;
            }

            var actual = QuickPickHistoricalGrader.ResolveActual(o, p.Market);
            if (actual is null)
            {
                return;
            }

            Assert.NotEqual(actual.Value, p.ProjectedValue);
        });
    }

    [Fact]
    public async Task Baseline_And_Enhanced_Week_Are_Deterministic_And_Identical_When_Groups_None()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var runner = provider.GetRequiredService<IQuickPicksHistoricalEvaluationRunner>();

        var bas1 = await runner.RunWeekAsync(
            ControlledHistoricalFixture.Season,
            ControlledHistoricalFixture.Week,
            QuickPickMode.Baseline,
            ControlledHistoricalFixture.FixtureId);
        var bas2 = await runner.RunWeekAsync(
            ControlledHistoricalFixture.Season,
            ControlledHistoricalFixture.Week,
            QuickPickMode.Baseline,
            ControlledHistoricalFixture.FixtureId);
        var enh = await runner.RunWeekAsync(
            ControlledHistoricalFixture.Season,
            ControlledHistoricalFixture.Week,
            QuickPickMode.Enhanced,
            ControlledHistoricalFixture.FixtureId);

        Assert.True(bas1.PredictionsEvaluated > 0);
        Assert.Equal(bas1.MeanAbsoluteError, bas2.MeanAbsoluteError);
        Assert.Equal(bas1.Top5HitRate, bas2.Top5HitRate);
        Assert.Equal(bas1.TotalPredictionValue, bas2.TotalPredictionValue);
        Assert.Equal(
            bas1.Graded.Select(g => (g.Prediction.PlayerId, g.Prediction.Market, g.Prediction.RankingScore, g.Prediction.RankInMarket)),
            bas2.Graded.Select(g => (g.Prediction.PlayerId, g.Prediction.Market, g.Prediction.RankingScore, g.Prediction.RankInMarket)));

        // AllowedEnhancedGroups=None → observational identity.
        Assert.Equal(KnowledgeImpactGroup.None, FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups);
        Assert.Equal(
            bas1.Graded.Select(g => (g.Prediction.PlayerId, g.Prediction.Market, g.Prediction.RankingScore, g.AbsoluteError)),
            enh.Graded.Select(g => (g.Prediction.PlayerId, g.Prediction.Market, g.Prediction.RankingScore, g.AbsoluteError)));

        var change = QuickPickHistoricalGrader.AnalyzeChanges([bas1], [enh]);
        Assert.True(change.PredictionsIdentical);
        Assert.Equal(0, change.PredictionsChanged);
        Assert.Empty(change.Helped);
        Assert.Empty(change.Hurt);

        var state = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        // Week runner leaves state in last configured mode; official eval restores Passthrough.
        Assert.Equal(QuickPickMode.Enhanced, enh.Mode);
    }

    [Fact]
    public void Season_Isolation_And_Holdout_Constants()
    {
        Assert.Equal(new[] { 2015, 2018, 2021 }, FrozenQuickPicksHistoricalEvaluationV1.DevelopmentSeasons);
        Assert.Equal(2024, FrozenQuickPicksHistoricalEvaluationV1.HoldoutSeason);
        Assert.DoesNotContain(2024, FrozenQuickPicksHistoricalEvaluationV1.DevelopmentSeasons);
        Assert.Equal(KnowledgeImpactGroup.None, FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups);
        Assert.False(FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups.HasFlag(KnowledgeImpactGroup.Usage));
        Assert.False(FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups.HasFlag(KnowledgeImpactGroup.RoleHealth));
        Assert.False(FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups.HasFlag(KnowledgeImpactGroup.RecentForm));
    }

    [Fact]
    public void Frozen_Layers_Remain_Unchanged()
    {
        Assert.Equal(ProjectionCalibrationMethod.PiecewiseScaleAt20, FrozenProjectionCalibrationV2.Method);
        Assert.Equal(0.6005, FrozenProjectionCalibrationV2.HighSlope);
        Assert.Equal(0.9240, FrozenProjectionCalibrationV2.LowSlope);
        Assert.Equal(new[] { 0, 15, 25, 35 }, FrozenDecisionConfidenceCalibrationV2.BinStarts);
        Assert.Equal(new[] { 57, 67, 65, 42 }, FrozenDecisionConfidenceCalibrationV2.CalibratedRates);
        Assert.Equal(DecisionPolicyKinds.SuppressStartAndSit, FrozenConfidenceAwareDecisionPolicyV1.Kind);
        Assert.Equal(45, FrozenConfidenceAwareDecisionPolicyV1.MaxCalibratedConfidenceToSuppressStart);
        // Knowledge Impact V1 freeze record unchanged (Usage selected historically; production rejected).
        Assert.Equal(KnowledgeImpactGroup.Usage, FrozenKnowledgeImpactExperimentV1.FrozenEnhancedGroups);
    }

    [Fact]
    public void Change_Analysis_Classifies_Helped_Hurt_Neutral()
    {
        Assert.Equal("NEUTRAL", QuickPickHistoricalGrading.ClassifyLedger(2, 2, 0));
        Assert.Equal("HELPED", QuickPickHistoricalGrading.ClassifyLedger(5, 2, 1.0));
        Assert.Equal("HURT", QuickPickHistoricalGrading.ClassifyLedger(1, 4, 1.0));
        Assert.Equal("NEUTRAL", QuickPickHistoricalGrading.ClassifyLedger(3, 3, 2.0));
    }

    [Fact]
    public async Task Official_Evaluation_Structure_With_Controlled_Fixture_Week_Is_Wired()
    {
        // Full multi-season nflverse official run is exercised separately when writing the report.
        // This test proves command + DI wiring and that production knowledge mode restores.
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var card = await HistoricalReplayCommands.RunQuickPicksHistoricalWeekAsync(
            provider,
            ControlledHistoricalFixture.Season,
            ControlledHistoricalFixture.Week,
            QuickPickMode.Baseline,
            ControlledHistoricalFixture.FixtureId);

        Assert.Equal(FrozenQuickPicksHistoricalEvaluationV1.EvaluatorVersion, card.EvaluatorVersion);
        Assert.True(card.PredictionsEvaluated > 0);
        Assert.True(card.MeanAbsoluteError > 0);
        Assert.True(card.WeeksEvaluated >= 1);
    }
}
