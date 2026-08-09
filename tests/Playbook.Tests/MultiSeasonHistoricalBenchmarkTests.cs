using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Players.Data;
using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class MultiSeasonHistoricalBenchmarkTests
{
    [Fact]
    public async Task Frozen_2018_Season_Benchmark_Remains_Unchanged()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var scorecard = await HistoricalReplayCommands.RunReal2018SeasonAsync(provider);

        Assert.Equal(Frozen2018SeasonBenchmark.FairProjectionCount, scorecard.FairProjectionCount);
        Assert.Equal(Frozen2018SeasonBenchmark.CurrentModelMae, scorecard.CurrentModelMae);
        Assert.Equal(Frozen2018SeasonBenchmark.BaselineAMae, scorecard.BaselineAMae);
        Assert.Equal(Frozen2018SeasonBenchmark.BaselineBMae, scorecard.BaselineBMae);
        Assert.Equal(Frozen2018SeasonBenchmark.CurrentModelSignedBias, scorecard.CurrentModelSignedBias);
        Assert.Equal(Frozen2018SeasonBenchmark.TotalDecisions, scorecard.TotalDecisions);
        Assert.Equal(Frozen2018SeasonBenchmark.CorrectDecisions, scorecard.CorrectDecisions);
        Assert.Equal(Frozen2018SeasonBenchmark.IncorrectDecisions, scorecard.IncorrectDecisions);
        Assert.Equal(Frozen2018SeasonBenchmark.DecisionAccuracyPercent, scorecard.DecisionAccuracyPercent);
        Assert.Equal(Frozen2018SeasonBenchmark.TotalDecisionValue, scorecard.TotalDecisionValue);
        Assert.Equal(Frozen2018SeasonBenchmark.AverageConfidence, scorecard.AverageConfidence);
        Assert.Equal(Frozen2018SeasonBenchmark.AverageDecisionValue, scorecard.AverageDecisionValue);
        Assert.Equal(Frozen2018SeasonBenchmark.MedianDecisionValue, scorecard.MedianDecisionValue);
    }

    [Fact]
    public void CrossSeasonBuilder_Computes_Baseline_HeadToHead_And_Findings()
    {
        var s2015 = MiniSeason(2015, currentMae: 12, baseA: 9, bias: -7, accuracy: 55, totalVal: 100);
        var s2018 = MiniSeason(2018, currentMae: 11.62, baseA: 9.03, bias: -8.53, accuracy: 60.6, totalVal: 300);
        var report = CrossSeasonBenchmarkBuilder.Build(
            new MultiSeasonBenchmarkRequest
            {
                Seasons = [2015, 2018],
                SeasonRoles = new Dictionary<int, EvaluationSeasonRole>
                {
                    [2015] = EvaluationSeasonRole.Development,
                    [2018] = EvaluationSeasonRole.FrozenBenchmark
                }
            },
            [s2015, s2018]);

        Assert.Equal(2, report.SeasonsBaselineAWins);
        Assert.Equal(0, report.SeasonsCurrentWins);
        Assert.Contains(report.BaselineComparisons, r => r.Scope == "ALL" && r.Winner == "Baseline A");
        Assert.Contains(
            report.StructuralFindings,
            f => f.Kind == StructuralFindingKind.ConfirmedStructuralProblem &&
                 f.Title.Contains("loses to simple recent-average", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            report.StructuralFindings,
            f => f.Kind == StructuralFindingKind.DataLimitation);
        Assert.Equal(EvaluationSeasonRole.FrozenBenchmark, report.SeasonRoles[2018]);
        Assert.True(report.CrossSeasonFailureLedger.Count >= 1);
        Assert.Contains("MODEL FROZEN", report.ToReportText());
    }

    [Fact]
    public async Task MultiSeason_Benchmark_Preserves_Cutoffs_And_Is_Deterministic()
    {
        using var p1 = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        using var p2 = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);

        // Narrow week window for speed while still covering two seasons.
        var request = new MultiSeasonBenchmarkRequest
        {
            Seasons = [2018, 2021],
            StartWeek = 5,
            EndWeek = 7,
            FixtureId = "nflverse",
            SeasonRoles = new Dictionary<int, EvaluationSeasonRole>
            {
                [2018] = EvaluationSeasonRole.FrozenBenchmark,
                [2021] = EvaluationSeasonRole.Development
            }
        };

        var a = await HistoricalReplayCommands.RunMultiSeasonBenchmarkAsync(p1, request);
        var b = await HistoricalReplayCommands.RunMultiSeasonBenchmarkAsync(p2, request);

        Assert.Equal(2, a.SeasonSummaries.Count);
        Assert.Equal(a.TotalDecisions, b.TotalDecisions);
        Assert.Equal(a.AggregateCurrentModelMae, b.AggregateCurrentModelMae);
        Assert.Equal(a.AggregateBaselineAMae, b.AggregateBaselineAMae);
        Assert.Equal(a.AggregateDecisionAccuracyPercent, b.AggregateDecisionAccuracyPercent);
        Assert.Equal(a.AggregateTotalDecisionValue, b.AggregateTotalDecisionValue);
        Assert.Equal(a.SeasonsBaselineAWins, b.SeasonsBaselineAWins);

        Assert.All(a.SeasonScorecards.SelectMany(s => s.ProjectionEvaluations), p =>
        {
            Assert.All(p.SourceWeeks, w => Assert.True(w < p.Week));
            Assert.DoesNotContain(p.Week, p.SourceWeeks);
            Assert.DoesNotContain(p.Week + 1, p.SourceWeeks);
        });

        var cutoffs = a.SeasonScorecards
            .SelectMany(s => s.WeekReports)
            .Select(w => (w.Season, w.Week, w.InformationCutoff))
            .ToList();
        Assert.True(cutoffs.Select(c => c.InformationCutoff).Distinct().Count() >= 2);
    }

    [Fact]
    public async Task Season_Calendar_Resolves_NonHardcoded_End_Week()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var calendar = provider.GetRequiredService<IHistoricalSeasonCalendar>();
        var end2018 = await calendar.GetRegularSeasonEndWeekAsync(2018);
        var end2021 = await calendar.GetRegularSeasonEndWeekAsync(2021);
        Assert.Equal(17, end2018);
        Assert.Equal(18, end2021);
    }

    [Fact]
    public async Task Synthetic_Leakage_And_Live_Paths_Still_Work()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var controlled = await HistoricalReplayCommands.RunControlled2018Week7Async(provider);
        Assert.True(controlled.DecisionCount > 0);

        var team = provider.GetRequiredService<IFantasyTeamIntelligenceService>();
        var report = team.GetReport();
        Assert.True(report.HasRosterPlayers);
    }

    [Fact]
    public async Task Default_MultiSeason_Benchmark_Runs_Diverse_Sample()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunDefaultMultiSeasonBenchmarkAsync(provider);

        Assert.Equal(DefaultMultiSeasonBenchmarkSample.Seasons, report.Seasons);
        Assert.True(report.TotalWeeksCompleted >= 60);
        Assert.True(report.TotalFairProjectionEvaluations >= 800);
        Assert.True(report.TotalDecisions >= 200);
        Assert.NotNull(report.AggregateCurrentModelMae);
        Assert.NotNull(report.AggregateBaselineAMae);
        Assert.NotNull(report.AggregateBias);
        Assert.Equal(5, report.ConfidenceBuckets.Count);
        Assert.True(report.CrossSeasonFailureLedger.Count > 0);
        Assert.Contains(report.StructuralFindings, f => f.Kind == StructuralFindingKind.DataLimitation);
        Assert.Equal(EvaluationSeasonRole.FrozenBenchmark, report.SeasonRoles[2018]);
        Assert.Equal(EvaluationSeasonRole.HoldoutTest, report.SeasonRoles[2024]);

        // 2018 row inside multi-season report must match frozen benchmark.
        var s2018 = report.SeasonSummaries.Single(s => s.Season == 2018);
        Assert.Equal(Frozen2018SeasonBenchmark.CurrentModelMae, s2018.CurrentModelMae);
        Assert.Equal(Frozen2018SeasonBenchmark.BaselineAMae, s2018.BaselineAMae);
        Assert.Equal(Frozen2018SeasonBenchmark.DecisionAccuracyPercent, s2018.DecisionAccuracyPercent);

        var text = report.ToReportText();
        Assert.Contains("MULTI-SEASON HISTORICAL BENCHMARK", text);
        Assert.Contains("BASELINE A HEAD-TO-HEAD", text);
        Assert.Contains("STRUCTURAL FINDINGS", text);

        var outPath = Path.Combine(AppContext.BaseDirectory, "MULTI_SEASON_BENCHMARK.txt");
        await File.WriteAllTextAsync(outPath, text);
        Assert.True(File.Exists(outPath));
    }

    private static SeasonScorecard MiniSeason(
        int season,
        double currentMae,
        double baseA,
        double bias,
        double accuracy,
        double totalVal)
    {
        var cutoff = new DateTimeOffset(season, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var grades = new List<ReplayDecisionGrade>
        {
            new()
            {
                DecisionId = Guid.NewGuid(),
                Season = season,
                Week = 7,
                InformationCutoff = cutoff,
                PlayerId = Guid.NewGuid(),
                PlayerName = "Alpha",
                Position = Core.Players.Position.WR,
                Recommendation = Core.Decisions.DecisionRecommendation.Start,
                Confidence = 30,
                ExpectedValue = 18,
                ActualFantasyPoints = 10,
                ProjectionAbsoluteError = 8,
                ProjectionSignedError = -8,
                ProjectionSquaredError = 64,
                DataSufficiency = DataSufficiency.Sufficient,
                ProjectionSourceWeeks = [1, 2, 3, 4, 5, 6],
                BaselineRecentAveragePoints = 12,
                BaselineOpportunityAwarePoints = 18,
                BaselineRecentAbsoluteError = 2,
                BaselineOpportunityAbsoluteError = 8,
                RecommendationMargin = 5,
                AlternativePlayerName = "Beta",
                AlternativeExpectedValue = 13,
                AlternativeActualFantasyPoints = 20,
                ActualDecisionDifferential = -10,
                WasCorrect = false,
                MarginMattered = true,
                EvaluationSummary = "INCORRECT",
                SupportingEvidence = ["usage"],
                OpposingEvidence = [],
                Unknowns = ["news unavailable"],
                Rationale = ["higher projection"]
            },
            new()
            {
                DecisionId = Guid.NewGuid(),
                Season = season,
                Week = 8,
                InformationCutoff = cutoff,
                PlayerId = Guid.NewGuid(),
                PlayerName = "Gamma",
                Position = Core.Players.Position.RB,
                Recommendation = Core.Decisions.DecisionRecommendation.Start,
                Confidence = 25,
                ExpectedValue = 16,
                ActualFantasyPoints = 20,
                ProjectionAbsoluteError = 4,
                ProjectionSignedError = 4,
                ProjectionSquaredError = 16,
                DataSufficiency = DataSufficiency.Sufficient,
                ProjectionSourceWeeks = [1, 2, 3, 4, 5, 6, 7],
                BaselineRecentAveragePoints = 15,
                BaselineOpportunityAwarePoints = 16,
                BaselineRecentAbsoluteError = 5,
                BaselineOpportunityAbsoluteError = 4,
                RecommendationMargin = 7,
                AlternativePlayerName = "Delta",
                AlternativeExpectedValue = 9,
                AlternativeActualFantasyPoints = 8,
                ActualDecisionDifferential = 12,
                WasCorrect = true,
                MarginMattered = true,
                EvaluationSummary = "CORRECT",
                SupportingEvidence = ["recent production"],
                OpposingEvidence = [],
                Unknowns = [],
                Rationale = ["edge"]
            }
        };

        var projections = new List<PlayerProjectionEvaluation>
        {
            new()
            {
                Season = season,
                Week = 7,
                PlayerId = grades[0].PlayerId,
                PlayerName = "Alpha",
                Position = Core.Players.Position.WR,
                PredictedPoints = 18,
                ActualPoints = 10,
                AbsoluteError = currentMae,
                SignedError = bias,
                SquaredError = currentMae * currentMae,
                BaselineRecentAveragePoints = 12,
                BaselineOpportunityAwarePoints = 18,
                BaselineRecentAbsoluteError = baseA,
                BaselineOpportunityAbsoluteError = currentMae,
                DataSufficiency = DataSufficiency.Sufficient,
                ProjectionConfidence = 50,
                SourceWeeks = [1, 2, 3, 4, 5, 6]
            },
            new()
            {
                Season = season,
                Week = 8,
                PlayerId = grades[1].PlayerId,
                PlayerName = "Gamma",
                Position = Core.Players.Position.RB,
                PredictedPoints = 16,
                ActualPoints = 20,
                AbsoluteError = currentMae,
                SignedError = bias,
                SquaredError = currentMae * currentMae,
                BaselineRecentAveragePoints = 15,
                BaselineOpportunityAwarePoints = 16,
                BaselineRecentAbsoluteError = baseA,
                BaselineOpportunityAbsoluteError = currentMae,
                DataSufficiency = DataSufficiency.Sufficient,
                ProjectionConfidence = 55,
                SourceWeeks = [1, 2, 3, 4, 5, 6, 7]
            }
        };

        var correct = (int)Math.Round(accuracy / 100.0 * 2);
        return new SeasonScorecard
        {
            Season = season,
            StartWeek = 1,
            EndWeek = 17,
            ScoringType = ScoringType.Ppr,
            FixtureId = "unit",
            GeneratedAt = DateTimeOffset.UtcNow,
            WeekReports = [],
            SkippedWeeks = [],
            Weeks = [],
            ProjectionEvaluations = projections,
            AllGrades = grades,
            AllDecisionRecords = [],
            FailureLedger =
            [
                new FailureLedgerEntry
                {
                    DecisionId = grades[0].DecisionId,
                    Season = season,
                    Week = 7,
                    InformationCutoff = cutoff,
                    PlayerId = grades[0].PlayerId,
                    PlayerName = "Alpha",
                    Position = Core.Players.Position.WR,
                    Recommendation = Core.Decisions.DecisionRecommendation.Start,
                    PredictedPoints = 18,
                    ActualPoints = 10,
                    Confidence = 30,
                    DataSufficiency = DataSufficiency.Sufficient,
                    AlternativePlayerName = "Beta",
                    AlternativePredictedPoints = 13,
                    AlternativeActualPoints = 20,
                    DecisionCost = -10,
                    EvaluationSummary = "INCORRECT",
                    SupportingEvidence = ["usage"],
                    OpposingEvidence = [],
                    Unknowns = ["news unavailable"],
                    Rationale = ["higher projection"],
                    ProjectionSourceWeeks = [1, 2, 3, 4, 5, 6]
                }
            ],
            ConfidenceBuckets = [],
            ByPosition = [],
            ObservablePatterns = [],
            DataQuality = new HistoricalDataQualityReport
            {
                WeeksRequested = 17,
                WeeksCompleted = 17,
                WeeksSkipped = 0,
                PlayersEvaluated = 20,
                PlayersWithValidProjection = 18,
                PercentPlayersWithValidProjection = 90,
                DecisionsGenerated = 2,
                DecisionsGraded = 2,
                DecisionsSkippedInsufficientData = 0,
                ProjectionEvaluations = 2,
                PercentWithInjurySignal = 5,
                PercentWithUsageSignal = 90,
                PercentWithRoleSignal = 90,
                PercentSufficientHistory = 80,
                PercentLimitedHistory = 20,
                PercentInsufficientHistory = 0,
                UnavailableInformation = ["News archive: UNAVAILABLE"],
                SkippedWeeks = []
            },
            FairProjectionCount = projections.Count,
            CurrentModelMae = currentMae,
            CurrentModelRmse = currentMae,
            CurrentModelSignedBias = bias,
            BaselineAMae = baseA,
            BaselineBMae = currentMae,
            BetterProjectionBaseline = "Baseline A (recent average)",
            TotalDecisions = 2,
            CorrectDecisions = correct,
            IncorrectDecisions = 2 - correct,
            UngradedDecisions = 0,
            DecisionAccuracyPercent = accuracy,
            AverageDecisionValue = 1,
            MedianDecisionValue = 1,
            TotalDecisionValue = totalVal,
            AverageConfidence = 27.5
        };
    }
}
