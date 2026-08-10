using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Players.Data;
using Playbook.Application.Predictions;
using Playbook.Core.Decisions;
using Playbook.Core.Knowledge;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Knowledge;
using Playbook.Infrastructure.Predictions;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class QuickPicksRecentFormExperimentTests
{
    [Fact]
    public void Experiment_Isolates_RecentForm_Only()
    {
        Assert.Equal(KnowledgeImpactGroup.RecentForm, FrozenQuickPicksRecentFormExperimentV1.ExperimentalGroups);
        Assert.False(FrozenQuickPicksRecentFormExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.Usage));
        Assert.False(FrozenQuickPicksRecentFormExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.RoleHealth));
        Assert.False(FrozenQuickPicksRecentFormExperimentV1.ExperimentalGroups.HasFlag(KnowledgeImpactGroup.Matchup));
        Assert.Equal(KnowledgeImpactGroup.None, FrozenQuickPicksHistoricalEvaluationV1.AllowedEnhancedGroups);
        Assert.Equal(65, FrozenQuickPicksRecentFormExperimentV1.HighThreshold);
        Assert.Equal(35, FrozenQuickPicksRecentFormExperimentV1.LowThreshold);
        Assert.Equal(0.6, FrozenQuickPicksRecentFormExperimentV1.QuickPickOpportunityDelta);
        Assert.Equal(new[] { 2015, 2018, 2021 }, FrozenQuickPicksRecentFormExperimentV1.DevelopmentSeasons);
        Assert.Equal(2024, FrozenQuickPicksRecentFormExperimentV1.HoldoutSeason);
        Assert.DoesNotContain(2024, FrozenQuickPicksRecentFormExperimentV1.DevelopmentSeasons);
    }

    [Fact]
    public void Frozen_Layers_Unchanged()
    {
        Assert.Equal(ProjectionCalibrationMethod.PiecewiseScaleAt20, FrozenProjectionCalibrationV2.Method);
        Assert.Equal(0.6005, FrozenProjectionCalibrationV2.HighSlope);
        Assert.Equal(0.9240, FrozenProjectionCalibrationV2.LowSlope);
        Assert.Equal(new[] { 0, 15, 25, 35 }, FrozenDecisionConfidenceCalibrationV2.BinStarts);
        Assert.Equal(new[] { 57, 67, 65, 42 }, FrozenDecisionConfidenceCalibrationV2.CalibratedRates);
        Assert.Equal(DecisionPolicyKinds.SuppressStartAndSit, FrozenConfidenceAwareDecisionPolicyV1.Kind);
        Assert.Equal(45, FrozenConfidenceAwareDecisionPolicyV1.MaxCalibratedConfidenceToSuppressStart);
    }

    [Fact]
    public void RecentForm_Applicator_Uses_Shared_Knowledge_Evidence()
    {
        var state = new KnowledgeImpactExperimentState();
        state.ConfigureEnhanced(KnowledgeImpactGroup.RecentForm);
        var applicator = new KnowledgeImpactApplicator(state);

        var high = BuildBridgeWithForm(80);
        var low = BuildBridgeWithForm(20);
        var mid = BuildBridgeWithForm(50);

        var highAdj = applicator.ApplyToQuickPickPrediction(high.Prediction, high.Context);
        var lowAdj = applicator.ApplyToQuickPickPrediction(low.Prediction, low.Context);
        var midAdj = applicator.ApplyToQuickPickPrediction(mid.Prediction, mid.Context);

        Assert.Equal(
            HistoricalQuickPickGenerator.BridgeBaseOpportunityScore + 0.6m,
            highAdj.OpportunityScore);
        Assert.Equal(
            HistoricalQuickPickGenerator.BridgeBaseOpportunityScore - 0.6m,
            lowAdj.OpportunityScore);
        Assert.Equal(HistoricalQuickPickGenerator.BridgeBaseOpportunityScore, midAdj.OpportunityScore);
    }

    [Fact]
    public void Historical_Generator_Maps_RecentForm_Onto_RankingScore()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var generator = provider.GetRequiredService<HistoricalQuickPickGenerator>();
        var knowledge = provider.GetRequiredService<KnowledgeImpactExperimentState>();

        var raw = ControlledHistoricalFixture.Create();
        var (snapshot, _) = new HistoricalSnapshotBuilder().Build(raw);

        knowledge.ConfigureBaseline();
        var baseline = generator.Generate(snapshot, QuickPickMode.Baseline);

        knowledge.ConfigureEnhanced(KnowledgeImpactGroup.RecentForm);
        var enhanced = generator.Generate(snapshot, QuickPickMode.Enhanced);

        Assert.NotEmpty(enhanced);
        Assert.All(enhanced, p => Assert.True(p.KnowledgeAttached));

        // Charlie WR recent production 78 → RecentForm+ → RankingScore = Projected + 0.6
        var charlieRec = enhanced.Single(p =>
            p.PlayerId == ControlledHistoricalFixture.CharlieWrId &&
            p.Market == PredictionMarketType.ReceivingYards);
        var charlieBase = baseline.Single(p =>
            p.PlayerId == ControlledHistoricalFixture.CharlieWrId &&
            p.Market == PredictionMarketType.ReceivingYards);
        Assert.Equal(charlieBase.ProjectedValue + 0.6, charlieRec.RankingScore, 3);

        // Delta WR recent production 42 → mid → unchanged ranking vs projected
        var deltaRec = enhanced.Single(p =>
            p.PlayerId == ControlledHistoricalFixture.DeltaWrId &&
            p.Market == PredictionMarketType.ReceivingYards);
        Assert.Equal(deltaRec.ProjectedValue, deltaRec.RankingScore, 3);

        // Future injury still excluded from Enhanced knowledge.
        Assert.Null(snapshot.Players.Single(p => p.PlayerId == ControlledHistoricalFixture.DeltaWrId).InjuryStatus);
        KnowledgeTemporalGuard.AssertNoFutureLeak(
            deltaRec.KnowledgeContext!.Knowledge,
            snapshot.InformationCutoff);
    }

    [Fact]
    public async Task Baseline_Vs_Enhanced_Isolation_On_Controlled_Week()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var runner = provider.GetRequiredService<IQuickPicksHistoricalEvaluationRunner>();

        var bas = await runner.RunWeekAsync(
            ControlledHistoricalFixture.Season,
            ControlledHistoricalFixture.Week,
            QuickPickMode.Baseline,
            ControlledHistoricalFixture.FixtureId);
        var enh = await runner.RunWeekAsync(
            ControlledHistoricalFixture.Season,
            ControlledHistoricalFixture.Week,
            QuickPickMode.Enhanced,
            ControlledHistoricalFixture.FixtureId,
            enhancedGroups: KnowledgeImpactGroup.RecentForm);

        Assert.Equal(KnowledgeImpactGroup.None, bas.ActiveGroups);
        Assert.Equal(KnowledgeImpactGroup.RecentForm, enh.ActiveGroups);
        Assert.Equal(bas.PredictionsEvaluated, enh.PredictionsEvaluated);

        var change = QuickPickHistoricalGrader.AnalyzeChanges([bas], [enh]);
        Assert.True(change.PredictionsChanged > 0);
        Assert.Contains(change.Changed, c => c.RecentFormValue is not null);

        // Repeated Baseline run is deterministic.
        var bas2 = await runner.RunWeekAsync(
            ControlledHistoricalFixture.Season,
            ControlledHistoricalFixture.Week,
            QuickPickMode.Baseline,
            ControlledHistoricalFixture.FixtureId);
        Assert.Equal(bas.MeanAbsoluteError, bas2.MeanAbsoluteError);
        Assert.Equal(
            bas.Graded.Select(g => (g.Prediction.RankingScore, g.Prediction.RankInMarket)),
            bas2.Graded.Select(g => (g.Prediction.RankingScore, g.Prediction.RankInMarket)));
    }

    [Fact]
    public async Task Official_RecentForm_Experiment_Runs_Once()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunQuickPicksRecentFormExperimentAsync(provider);

        Assert.Equal(FrozenQuickPicksRecentFormExperimentV1.ExperimentId, report.EvaluationId);
        Assert.False(report.UsedHoldoutDuringDevelopment);
        Assert.False(report.RejectedKnowledgeTransformsReenabled);
        Assert.True(report.ProjectionV2Unchanged);
        Assert.True(report.ConfidenceV2Unchanged);
        Assert.True(report.DecisionPolicyV1Unchanged);
        Assert.Equal(KnowledgeImpactGroup.RecentForm, report.AllowedEnhancedGroups);
        Assert.Equal(2024, report.HoldoutSeason);
        Assert.Equal(new[] { 2015, 2018, 2021 }, report.DevelopmentSeasons);

        Assert.True(report.DevelopmentChangeAnalysis.PredictionsChanged > 0);
        Assert.True(report.HoldoutChangeAnalysis.PredictionsChanged > 0);
        Assert.True(
            report.Verdict.StartsWith("IMPROVEMENT", StringComparison.Ordinal) ||
            report.Verdict.StartsWith("NEUTRAL", StringComparison.Ordinal) ||
            report.Verdict.StartsWith("REGRESSION", StringComparison.Ordinal));
        Assert.True(
            report.Verdict.Contains("ENABLED", StringComparison.Ordinal) ||
            report.Verdict.Contains("DISABLED", StringComparison.Ordinal));

        var state = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        Assert.Equal(KnowledgeMode.Passthrough, state.Mode);

        var text = report.ToReportText();
        Assert.Contains("QUICK PICKS HISTORICAL EVALUATION", text);
        Assert.Contains("OFFICIAL HOLDOUT 2024", text);

        var outPath = Path.Combine(AppContext.BaseDirectory, "QUICK_PICKS_RECENT_FORM_EXPERIMENT_V1_REPORT.txt");
        await File.WriteAllTextAsync(outPath, text);
        Assert.True(File.Exists(outPath));
    }

    private static (Prediction Prediction, PredictionContext Context) BuildBridgeWithForm(double formValue)
    {
        var cutoff = new DateTimeOffset(2018, 10, 16, 16, 0, 0, TimeSpan.Zero);
        var evt = new FootballEvent
        {
            EventId = "test-rf",
            HomeTeam = "KC",
            AwayTeam = "NE",
            CommenceTime = cutoff,
            Season = 2018,
            Phase = NflSeasonPhase.RegularSeason,
            Week = 7
        };
        var prediction = new Prediction
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Event = evt,
            PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PlayerName = "Test",
            Market = PredictionMarketType.ReceivingYards,
            PlaybookProjection = 70m,
            Probability = 50,
            Edge = 0m,
            Confidence = 60,
            Direction = PredictionDirection.Over,
            Reasoning = "test",
            SupportingIntelligence = [],
            CalculationNotes = [],
            Source = "test",
            LineFreshness = PropLineFreshness.Mock,
            LastUpdated = cutoff,
            OpportunityScore = HistoricalQuickPickGenerator.BridgeBaseOpportunityScore
        };

        var bundle = new SharedKnowledgeBundle
        {
            PlayerId = prediction.PlayerId,
            PlayerName = prediction.PlayerName,
            Position = Position.WR,
            Season = 2018,
            Week = 7,
            InformationCutoff = cutoff,
            GeneratedAt = cutoff,
            Facts = [],
            Evidence =
            [
                new KnowledgeEvidence
                {
                    Scope = KnowledgeScope.Player,
                    Aspect = KnowledgeAspect.RecentProduction,
                    Statement = $"Recent production score {formValue}/100.",
                    Direction = formValue >= 65 ? SignalDirection.Positive :
                        formValue <= 35 ? SignalDirection.Negative : SignalDirection.Neutral,
                    Strength = SignalStrength.Moderate,
                    Status = EvidenceStatus.Known,
                    Confidence = 60,
                    Reliability = EvidenceReliability.Moderate,
                    Source = "test",
                    ObservedAt = cutoff.AddHours(-1),
                    InformationCutoff = cutoff,
                    Value = formValue
                }
            ],
            UnavailableAspects = [],
            UnavailableSources = [],
            OverallStatus = EvidenceStatus.Known,
            KnowledgeConfidence = 60
        };

        var ctx = new PredictionContext
        {
            PredictionType = PredictionType.QuickPick,
            Season = 2018,
            Week = 7,
            InformationCutoff = cutoff,
            PlayerId = prediction.PlayerId,
            PlayerName = prediction.PlayerName,
            Knowledge = bundle,
            GeneratedAt = cutoff
        };

        return (prediction, ctx);
    }
}
