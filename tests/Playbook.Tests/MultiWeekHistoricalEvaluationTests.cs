using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Players.Data;
using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class MultiWeekHistoricalEvaluationTests
{
    [Fact]
    public void SeasonScorecardBuilder_Computes_Fair_Baselines_And_Confidence_Buckets()
    {
        var week = BuildSyntheticWeekReport(season: 2018, week: 5);
        var scorecard = SeasonScorecardBuilder.Build(
            new MultiWeekReplayRequest
            {
                Season = 2018,
                StartWeek = 5,
                EndWeek = 5,
                FixtureId = "unit"
            },
            [week],
            []);

        Assert.Equal(2, scorecard.FairProjectionCount);
        Assert.NotNull(scorecard.CurrentModelMae);
        Assert.NotNull(scorecard.BaselineAMae);
        Assert.NotNull(scorecard.BaselineBMae);
        Assert.Equal(scorecard.TotalDecisions, scorecard.ConfidenceBuckets.Sum(b => b.DecisionCount));
        Assert.Contains(scorecard.FailureLedger, f =>
            f.EvaluationSummary.Contains("INCORRECT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, scorecard.IncorrectDecisions);
        Assert.Equal(1, scorecard.CorrectDecisions);
    }

    [Fact]
    public async Task MultiWeek_Runner_Preserves_Independent_Cutoffs_And_No_Future_Source_Weeks()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var scorecard = await HistoricalReplayCommands.RunSeasonAsync(
            provider,
            season: 2018,
            startWeek: 6,
            endWeek: 8,
            fixtureId: "nflverse");

        Assert.True(scorecard.DataQuality.WeeksCompleted >= 2);
        Assert.All(scorecard.WeekReports, report =>
        {
            Assert.All(report.ProjectionEvaluations, p =>
            {
                Assert.All(p.SourceWeeks, w => Assert.True(w < report.Week));
                Assert.DoesNotContain(report.Week, p.SourceWeeks);
                Assert.DoesNotContain(report.Week + 1, p.SourceWeeks);
            });
            Assert.All(report.Grades, g =>
            {
                Assert.Equal(report.Season, g.Season);
                Assert.Equal(report.Week, g.Week);
                Assert.Equal(report.InformationCutoff, g.InformationCutoff);
                Assert.All(g.ProjectionSourceWeeks, w => Assert.True(w < report.Week));
            });
            Assert.All(report.DecisionRecords, r =>
            {
                Assert.NotNull(r.ActualOutcome);
                Assert.Equal(report.InformationCutoff, r.InformationCutoff);
            });
        });

        // Distinct cutoffs across weeks.
        var cutoffs = scorecard.WeekReports.Select(w => w.InformationCutoff).Distinct().ToList();
        Assert.True(cutoffs.Count >= 2);
    }

    [Fact]
    public async Task MultiWeek_Runner_Is_Deterministic_Across_Two_Runs()
    {
        using var p1 = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        using var p2 = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);

        var a = await HistoricalReplayCommands.RunSeasonAsync(p1, 2018, 5, 7, fixtureId: "nflverse");
        var b = await HistoricalReplayCommands.RunSeasonAsync(p2, 2018, 5, 7, fixtureId: "nflverse");

        Assert.Equal(a.DataQuality.WeeksCompleted, b.DataQuality.WeeksCompleted);
        Assert.Equal(a.TotalDecisions, b.TotalDecisions);
        Assert.Equal(a.CorrectDecisions, b.CorrectDecisions);
        Assert.Equal(a.DecisionAccuracyPercent, b.DecisionAccuracyPercent);
        Assert.Equal(a.CurrentModelMae, b.CurrentModelMae);
        Assert.Equal(a.BaselineAMae, b.BaselineAMae);
        Assert.Equal(a.BaselineBMae, b.BaselineBMae);
        Assert.Equal(a.AverageConfidence, b.AverageConfidence);
        Assert.Equal(a.FailureLedger.Count, b.FailureLedger.Count);
    }

    [Fact]
    public async Task Original_2018_Week7_Replay_Still_Matches_Standalone()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var standalone = await HistoricalReplayCommands.RunReal2018Week7Async(provider);
        var seasonSlice = await HistoricalReplayCommands.RunSeasonAsync(
            provider, 2018, 7, 7, fixtureId: "nflverse");

        var week = Assert.Single(seasonSlice.WeekReports);
        Assert.Equal(standalone.DecisionCount, week.DecisionCount);
        Assert.Equal(standalone.CorrectCount, week.CorrectCount);
        Assert.Equal(standalone.IncorrectCount, week.IncorrectCount);
        Assert.Equal(standalone.DecisionAccuracyPercent, week.DecisionAccuracyPercent);
        Assert.Equal(standalone.AverageConfidence, week.AverageConfidence);
        Assert.Equal(standalone.AverageProjectionAbsoluteError, week.AverageProjectionAbsoluteError);
    }

    [Fact]
    public async Task Synthetic_Leakage_Fixture_Still_Passes()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunControlled2018Week7Async(provider);
        Assert.True(report.DecisionCount > 0);
        Assert.DoesNotContain(
            report.Grades.SelectMany(g => g.SupportingEvidence.Concat(g.OpposingEvidence).Concat(g.Rationale)),
            text => text.Contains("ruled out for the season", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Full_2018_Season_Produces_Scorecard_And_Failure_Ledger()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var scorecard = await HistoricalReplayCommands.RunReal2018SeasonAsync(provider);

        Assert.Equal(2018, scorecard.Season);
        Assert.Equal(1, scorecard.StartWeek);
        Assert.Equal(17, scorecard.EndWeek);
        Assert.True(scorecard.DataQuality.WeeksCompleted >= 14);
        Assert.True(scorecard.TotalDecisions >= 50);
        Assert.True(scorecard.FairProjectionCount >= 50);
        Assert.NotNull(scorecard.CurrentModelMae);
        Assert.NotNull(scorecard.BaselineAMae);
        Assert.NotNull(scorecard.BaselineBMae);
        Assert.NotNull(scorecard.DecisionAccuracyPercent);
        Assert.Equal(5, scorecard.ConfidenceBuckets.Count);
        Assert.True(scorecard.FailureLedger.Count > 0);
        Assert.Contains(scorecard.ByPosition, p => p.Position == Core.Players.Position.RB);
        Assert.Contains(scorecard.DataQuality.UnavailableInformation, s =>
            s.Contains("News archive", StringComparison.OrdinalIgnoreCase));

        // Week N never uses Week N / N+1 in source weeks across the season.
        Assert.All(scorecard.ProjectionEvaluations, p =>
        {
            Assert.All(p.SourceWeeks, w => Assert.True(w < p.Week));
            Assert.DoesNotContain(p.Week, p.SourceWeeks);
        });

        var text = scorecard.ToScorecardText();
        Assert.Contains("SEASON SCORECARD", text);
        Assert.Contains("PROJECTION", text);
        Assert.Contains("CONFIDENCE CALIBRATION", text);
        Assert.Contains("FAILURE LEDGER", text);

        // Persist developer scorecard artifact for review (not a UI).
        var outPath = Path.Combine(AppContext.BaseDirectory, "SEASON_SCORECARD_2018.txt");
        await File.WriteAllTextAsync(outPath, text);
        Assert.True(File.Exists(outPath));
    }

    private static HistoricalReplayReport BuildSyntheticWeekReport(int season, int week)
    {
        var cutoff = new DateTimeOffset(season, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var grades = new List<ReplayDecisionGrade>
        {
            new()
            {
                DecisionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Season = season,
                Week = week,
                InformationCutoff = cutoff,
                PlayerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                PlayerName = "Alpha",
                Position = Core.Players.Position.WR,
                Recommendation = Core.Decisions.DecisionRecommendation.Start,
                Confidence = 55,
                ExpectedValue = 15,
                ActualFantasyPoints = 18,
                ProjectionAbsoluteError = 3,
                ProjectionSignedError = 3,
                ProjectionSquaredError = 9,
                DataSufficiency = DataSufficiency.Sufficient,
                ProjectionSourceWeeks = [1, 2, 3, 4],
                BaselineRecentAveragePoints = 14,
                BaselineOpportunityAwarePoints = 15,
                BaselineRecentAbsoluteError = 4,
                BaselineOpportunityAbsoluteError = 3,
                RecommendationMargin = 4,
                AlternativePlayerName = "Beta",
                AlternativeExpectedValue = 11,
                AlternativeActualFantasyPoints = 10,
                ActualDecisionDifferential = 8,
                WasCorrect = true,
                MarginMattered = true,
                EvaluationSummary = "CORRECT",
                SupportingEvidence = ["recent production"],
                OpposingEvidence = [],
                Unknowns = ["news unavailable"],
                Rationale = ["higher projection"]
            },
            new()
            {
                DecisionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Season = season,
                Week = week,
                InformationCutoff = cutoff,
                PlayerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                PlayerName = "Gamma",
                Position = Core.Players.Position.RB,
                Recommendation = Core.Decisions.DecisionRecommendation.Start,
                Confidence = 25,
                ExpectedValue = 12,
                ActualFantasyPoints = 6,
                ProjectionAbsoluteError = 6,
                ProjectionSignedError = -6,
                ProjectionSquaredError = 36,
                DataSufficiency = DataSufficiency.Limited,
                ProjectionSourceWeeks = [3, 4],
                BaselineRecentAveragePoints = 10,
                BaselineOpportunityAwarePoints = 12,
                BaselineRecentAbsoluteError = 4,
                BaselineOpportunityAbsoluteError = 6,
                RecommendationMargin = 1,
                AlternativePlayerName = "Delta",
                AlternativeExpectedValue = 11,
                AlternativeActualFantasyPoints = 14,
                ActualDecisionDifferential = -8,
                WasCorrect = false,
                MarginMattered = true,
                EvaluationSummary = "INCORRECT",
                SupportingEvidence = ["usage"],
                OpposingEvidence = ["volatility"],
                Unknowns = [],
                Rationale = ["slight edge"]
            }
        };

        var projections = new List<PlayerProjectionEvaluation>
        {
            new()
            {
                Season = season,
                Week = week,
                PlayerId = grades[0].PlayerId,
                PlayerName = "Alpha",
                Position = Core.Players.Position.WR,
                PredictedPoints = 15,
                ActualPoints = 18,
                AbsoluteError = 3,
                SignedError = 3,
                SquaredError = 9,
                BaselineRecentAveragePoints = 14,
                BaselineOpportunityAwarePoints = 15,
                BaselineRecentAbsoluteError = 4,
                BaselineOpportunityAbsoluteError = 3,
                DataSufficiency = DataSufficiency.Sufficient,
                SourceWeeks = [1, 2, 3, 4]
            },
            new()
            {
                Season = season,
                Week = week,
                PlayerId = grades[1].PlayerId,
                PlayerName = "Gamma",
                Position = Core.Players.Position.RB,
                PredictedPoints = 12,
                ActualPoints = 6,
                AbsoluteError = 6,
                SignedError = -6,
                SquaredError = 36,
                BaselineRecentAveragePoints = 10,
                BaselineOpportunityAwarePoints = 12,
                BaselineRecentAbsoluteError = 4,
                BaselineOpportunityAbsoluteError = 6,
                DataSufficiency = DataSufficiency.Limited,
                SourceWeeks = [3, 4]
            }
        };

        return new HistoricalReplayReport
        {
            Season = season,
            Week = week,
            InformationCutoff = cutoff,
            LeagueName = "unit",
            ScoringType = ScoringType.Ppr,
            DecisionCount = grades.Count,
            CorrectCount = 1,
            IncorrectCount = 1,
            UngradedCount = 0,
            DecisionAccuracyPercent = 50,
            AverageProjectionAbsoluteError = 4.5,
            AverageProjectionSquaredError = 22.5,
            BaselineRecentAverageMae = 4,
            BaselineOpportunityAwareMae = 4.5,
            BetterBaselineLabel = "Baseline A (recent average)",
            AverageDecisionDifferential = 0,
            AverageConfidence = 40,
            Grades = grades,
            DecisionRecords = [],
            ProjectionEvaluations = projections,
            PlayersEvaluated = 2,
            PlayersWithValidProjection = 2,
            PlayersWithInjurySignal = 0,
            PlayersWithUsageSignal = 2,
            PlayersWithRoleSignal = 1,
            UnavailableSources = ["News archive: UNAVAILABLE"],
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }
}
