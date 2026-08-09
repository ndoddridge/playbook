using Playbook.Core.Players;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Aggregates frozen-model season scorecards into a cross-season benchmark report.
/// Diagnosis only — does not tune formulas.
/// </summary>
public static class CrossSeasonBenchmarkBuilder
{
    private static readonly (string Label, int Min, int MaxExclusive)[] ConfidenceBucketDefs =
    [
        ("0-20%", 0, 20),
        ("20-40%", 20, 40),
        ("40-60%", 40, 60),
        ("60-80%", 60, 80),
        ("80-100%", 80, 101)
    ];

    public static MultiSeasonBenchmarkReport Build(
        MultiSeasonBenchmarkRequest request,
        IReadOnlyList<SeasonScorecard> scorecards)
    {
        var roles = request.SeasonRoles ?? new Dictionary<int, EvaluationSeasonRole>();
        var summaries = scorecards.Select(sc => ToSummary(sc, RoleFor(sc.Season, roles))).ToList();
        var allFair = scorecards.SelectMany(FairSet).ToList();
        var allGrades = scorecards.SelectMany(s => s.AllGrades).ToList();
        var allFailures = scorecards
            .SelectMany(s => s.FailureLedger)
            .OrderBy(f => f.DecisionCost ?? 0)
            .ThenBy(f => f.Season)
            .ThenBy(f => f.Week)
            .ToList();

        var comparisons = BuildBaselineComparisons(summaries, allFair);
        var seasonsTied = summaries.Count(s =>
            s.CurrentModelMae is not null &&
            s.BaselineAMae is not null &&
            Math.Abs(s.CurrentModelMae.Value - s.BaselineAMae.Value) < 0.005);
        var seasonsCurrentWins = summaries.Count(s =>
            s.CurrentModelMae is not null &&
            s.BaselineAMae is not null &&
            s.CurrentModelMae.Value + 0.005 < s.BaselineAMae.Value);
        var seasonsBaselineWins = summaries.Count(s =>
            s.CurrentModelMae is not null &&
            s.BaselineAMae is not null &&
            s.BaselineAMae.Value + 0.005 < s.CurrentModelMae.Value);

        var gradedDiffs = allGrades
            .Where(g => g.ActualDecisionDifferential is not null)
            .Select(g => g.ActualDecisionDifferential!.Value)
            .OrderBy(v => v)
            .ToList();
        var correct = allGrades.Count(g => g.WasCorrect == true);
        var graded = allGrades.Count(g => g.WasCorrect is not null);

        return new MultiSeasonBenchmarkReport
        {
            Seasons = scorecards.Select(s => s.Season).ToList(),
            ScoringType = request.ScoringType,
            FixtureId = request.FixtureId,
            GeneratedAt = DateTimeOffset.UtcNow,
            ModelFreezeNote =
                "MODEL FROZEN — projection/decision/confidence formulas unchanged for this benchmark. " +
                "Diagnosis only; no tuning against these seasons.",
            SeasonSummaries = summaries,
            SeasonScorecards = scorecards,
            BaselineComparisons = comparisons,
            SeasonsCurrentWins = seasonsCurrentWins,
            SeasonsBaselineAWins = seasonsBaselineWins,
            SeasonsTied = seasonsTied,
            BiasBreakdown = BuildBiasBreakdown(allFair, allGrades),
            DecisionBreakdown = BuildDecisionBreakdown(allGrades),
            ConfidenceBuckets = BuildConfidenceBuckets(allGrades),
            CrossSeasonFailureLedger = allFailures,
            LargestProjectionErrors = allFair
                .OrderByDescending(p => p.AbsoluteError)
                .Take(50)
                .ToList(),
            StructuralFindings = BuildStructuralFindings(summaries, allFair, allGrades),
            SeasonRoles = seasonsRolesMap(request.Seasons, roles),
            TotalWeeksCompleted = scorecards.Sum(s => s.DataQuality.WeeksCompleted),
            TotalFairProjectionEvaluations = allFair.Count,
            TotalDecisions = allGrades.Count,
            TotalGradedDecisions = graded,
            AggregateCurrentModelMae = AvgOrNull(allFair.Select(p => p.AbsoluteError)),
            AggregateBaselineAMae = AvgOrNull(allFair.Select(p => p.BaselineRecentAbsoluteError!.Value)),
            AggregateBaselineBMae = AvgOrNull(allFair.Select(p => p.BaselineOpportunityAbsoluteError!.Value)),
            AggregateBias = AvgOrNull(allFair.Select(p => p.SignedError)),
            AggregateDecisionAccuracyPercent = graded == 0 ? null : Math.Round(100.0 * correct / graded, 1),
            AggregateAverageDecisionValue = gradedDiffs.Count == 0 ? null : Math.Round(gradedDiffs.Average(), 2),
            AggregateMedianDecisionValue = gradedDiffs.Count == 0 ? null : Math.Round(Median(gradedDiffs), 2),
            AggregateTotalDecisionValue = gradedDiffs.Count == 0 ? null : Math.Round(gradedDiffs.Sum(), 2),
            AggregateWorstDecisionCost = gradedDiffs.Count == 0 ? null : Math.Round(gradedDiffs.First(), 2),
            AggregateBestDecisionValue = gradedDiffs.Count == 0 ? null : Math.Round(gradedDiffs.Last(), 2),
            AggregateAverageConfidence = allGrades.Count == 0
                ? 0
                : Math.Round(allGrades.Average(g => g.Confidence), 1)
        };

        static IReadOnlyDictionary<int, EvaluationSeasonRole> seasonsRolesMap(
            IReadOnlyList<int> seasons,
            IReadOnlyDictionary<int, EvaluationSeasonRole> roles)
        {
            var map = new Dictionary<int, EvaluationSeasonRole>();
            foreach (var season in seasons)
            {
                map[season] = RoleFor(season, roles);
            }

            return map;
        }
    }

    private static EvaluationSeasonRole RoleFor(
        int season,
        IReadOnlyDictionary<int, EvaluationSeasonRole> roles) =>
        roles.TryGetValue(season, out var role) ? role : EvaluationSeasonRole.Development;

    private static IEnumerable<PlayerProjectionEvaluation> FairSet(SeasonScorecard card) =>
        card.ProjectionEvaluations.Where(p =>
            p.BaselineRecentAbsoluteError is not null &&
            p.BaselineOpportunityAbsoluteError is not null);

    private static SeasonBenchmarkSummary ToSummary(SeasonScorecard sc, EvaluationSeasonRole role)
    {
        var diffs = sc.AllGrades
            .Where(g => g.ActualDecisionDifferential is not null)
            .Select(g => g.ActualDecisionDifferential!.Value)
            .OrderBy(v => v)
            .ToList();
        var graded = sc.CorrectDecisions + sc.IncorrectDecisions;
        double? delta = sc.CurrentModelMae is null || sc.BaselineAMae is null
            ? null
            : Math.Round(sc.CurrentModelMae.Value - sc.BaselineAMae.Value, 2);
        double? pct = sc.CurrentModelMae is null || sc.BaselineAMae is null || sc.BaselineAMae == 0
            ? null
            : Math.Round(100.0 * (sc.CurrentModelMae.Value - sc.BaselineAMae.Value) / sc.BaselineAMae.Value, 1);
        bool? beats = null;
        if (sc.CurrentModelMae is not null && sc.BaselineAMae is not null)
        {
            if (Math.Abs(sc.CurrentModelMae.Value - sc.BaselineAMae.Value) < 0.005)
            {
                beats = null;
            }
            else
            {
                beats = sc.CurrentModelMae.Value < sc.BaselineAMae.Value;
            }
        }

        return new SeasonBenchmarkSummary
        {
            Season = sc.Season,
            Role = role,
            WeeksCompleted = sc.DataQuality.WeeksCompleted,
            WeeksRequested = sc.DataQuality.WeeksRequested,
            FairProjectionCount = sc.FairProjectionCount,
            CurrentModelMae = sc.CurrentModelMae,
            BaselineAMae = sc.BaselineAMae,
            BaselineBMae = sc.BaselineBMae,
            Bias = sc.CurrentModelSignedBias,
            MaeDeltaVsBaselineA = delta,
            MaePctChangeVsBaselineA = pct,
            CurrentBeatsBaselineA = beats,
            TotalDecisions = sc.TotalDecisions,
            GradedDecisions = graded,
            DecisionAccuracyPercent = sc.DecisionAccuracyPercent,
            AverageDecisionValue = sc.AverageDecisionValue,
            MedianDecisionValue = sc.MedianDecisionValue,
            TotalDecisionValue = sc.TotalDecisionValue,
            WorstDecisionCost = diffs.Count == 0 ? null : Math.Round(diffs.First(), 2),
            BestDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Last(), 2),
            AverageConfidence = sc.AverageConfidence,
            PlayersEvaluated = sc.DataQuality.PlayersEvaluated,
            PlayersWithValidProjection = sc.DataQuality.PlayersWithValidProjection,
            PercentPlayersWithValidProjection = sc.DataQuality.PercentPlayersWithValidProjection,
            Scorecard = sc
        };
    }

    private static IReadOnlyList<BaselineComparisonRow> BuildBaselineComparisons(
        IReadOnlyList<SeasonBenchmarkSummary> summaries,
        IReadOnlyList<PlayerProjectionEvaluation> allFair)
    {
        var rows = new List<BaselineComparisonRow>();
        foreach (var s in summaries)
        {
            rows.Add(new BaselineComparisonRow
            {
                Scope = s.Season.ToString(),
                FairProjectionCount = s.FairProjectionCount,
                CurrentModelMae = s.CurrentModelMae,
                BaselineAMae = s.BaselineAMae,
                AbsoluteDifference = s.MaeDeltaVsBaselineA,
                PercentChangeVsBaselineA = s.MaePctChangeVsBaselineA,
                Winner = WinnerLabel(s.CurrentModelMae, s.BaselineAMae)
            });
        }

        var aggCurrent = AvgOrNull(allFair.Select(p => p.AbsoluteError));
        var aggA = AvgOrNull(allFair.Select(p => p.BaselineRecentAbsoluteError!.Value));
        double? abs = aggCurrent is null || aggA is null ? null : Math.Round(aggCurrent.Value - aggA.Value, 2);
        double? pct = aggCurrent is null || aggA is null || aggA == 0
            ? null
            : Math.Round(100.0 * (aggCurrent.Value - aggA.Value) / aggA.Value, 1);
        rows.Add(new BaselineComparisonRow
        {
            Scope = "ALL",
            FairProjectionCount = allFair.Count,
            CurrentModelMae = aggCurrent,
            BaselineAMae = aggA,
            AbsoluteDifference = abs,
            PercentChangeVsBaselineA = pct,
            Winner = WinnerLabel(aggCurrent, aggA)
        });
        return rows;
    }

    private static string WinnerLabel(double? current, double? baselineA)
    {
        if (current is null || baselineA is null)
        {
            return "n/a";
        }

        if (Math.Abs(current.Value - baselineA.Value) < 0.005)
        {
            return "Tie";
        }

        return current.Value < baselineA.Value ? "Current model" : "Baseline A";
    }

    private static IReadOnlyList<BiasBreakdownRow> BuildBiasBreakdown(
        IReadOnlyList<PlayerProjectionEvaluation> fair,
        IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var rows = new List<BiasBreakdownRow>();

        foreach (var group in fair.GroupBy(p => p.Season).OrderBy(g => g.Key))
        {
            rows.Add(BiasRow("Season", group.Key.ToString(), group.ToList()));
        }

        foreach (var pos in new[] { Position.QB, Position.RB, Position.WR, Position.TE })
        {
            rows.Add(BiasRow("Position", pos.ToString(), fair.Where(p => p.Position == pos).ToList()));
        }

        rows.Add(BiasRow("Week", "W1-6", fair.Where(p => p.Week <= 6).ToList()));
        rows.Add(BiasRow("Week", "W7-12", fair.Where(p => p.Week is >= 7 and <= 12).ToList()));
        rows.Add(BiasRow("Week", "W13+", fair.Where(p => p.Week >= 13).ToList()));

        rows.Add(BiasRow("ProjectionRange", "<10", fair.Where(p => p.PredictedPoints < 10).ToList()));
        rows.Add(BiasRow("ProjectionRange", "10-20", fair.Where(p => p.PredictedPoints is >= 10 and < 20).ToList()));
        rows.Add(BiasRow("ProjectionRange", ">=20", fair.Where(p => p.PredictedPoints >= 20).ToList()));

        rows.Add(BiasRow(
            "DataSufficiency",
            "Limited",
            fair.Where(p => p.DataSufficiency == DataSufficiency.Limited).ToList()));
        rows.Add(BiasRow(
            "DataSufficiency",
            "Sufficient",
            fair.Where(p => p.DataSufficiency == DataSufficiency.Sufficient).ToList()));

        // Confidence of the projection itself (not decision confidence), when present.
        rows.Add(BiasRow(
            "ProjectionConfidence",
            "<40",
            fair.Where(p => p.ProjectionConfidence is < 40).ToList()));
        rows.Add(BiasRow(
            "ProjectionConfidence",
            ">=40",
            fair.Where(p => p.ProjectionConfidence is >= 40).ToList()));

        _ = grades; // reserved for future decision-linked bias slices
        return rows.Where(r => r.SampleSize > 0).ToList();
    }

    private static BiasBreakdownRow BiasRow(
        string dimension,
        string bucket,
        IReadOnlyList<PlayerProjectionEvaluation> rows)
    {
        var bias = AvgOrNull(rows.Select(p => p.SignedError));
        var mae = AvgOrNull(rows.Select(p => p.AbsoluteError));
        var note = bias is null
            ? "n/a"
            : bias < -1
                ? "Observable over-projection (actual < predicted)."
                : bias > 1
                    ? "Observable under-projection (actual > predicted)."
                    : "Near-zero mean bias in this slice.";
        return new BiasBreakdownRow
        {
            Dimension = dimension,
            Bucket = bucket,
            SampleSize = rows.Count,
            MeanSignedBias = bias,
            Mae = mae,
            Notes = note
        };
    }

    private static IReadOnlyList<DecisionBreakdownRow> BuildDecisionBreakdown(
        IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var rows = new List<DecisionBreakdownRow>();
        foreach (var group in grades.GroupBy(g => g.Season).OrderBy(g => g.Key))
        {
            rows.Add(DecisionRow("Season", group.Key.ToString(), group.ToList()));
        }

        foreach (var pos in new[] { Position.QB, Position.RB, Position.WR, Position.TE })
        {
            rows.Add(DecisionRow("Position", pos.ToString(), grades.Where(g => g.Position == pos).ToList()));
        }

        rows.Add(DecisionRow("Confidence", "0-20", grades.Where(g => g.Confidence < 20).ToList()));
        rows.Add(DecisionRow("Confidence", "20-40", grades.Where(g => g.Confidence is >= 20 and < 40).ToList()));
        rows.Add(DecisionRow("Confidence", "40+", grades.Where(g => g.Confidence >= 40).ToList()));

        rows.Add(DecisionRow(
            "RecommendationMargin",
            "weak(<3)",
            grades.Where(g => g.RecommendationMargin is < 3).ToList()));
        rows.Add(DecisionRow(
            "RecommendationMargin",
            "mid(3-6)",
            grades.Where(g => g.RecommendationMargin is >= 3 and < 6).ToList()));
        rows.Add(DecisionRow(
            "RecommendationMargin",
            "strong(>=6)",
            grades.Where(g => g.RecommendationMargin is >= 6).ToList()));

        rows.Add(DecisionRow(
            "DataSufficiency",
            "Limited",
            grades.Where(g => g.DataSufficiency == DataSufficiency.Limited).ToList()));
        rows.Add(DecisionRow(
            "DataSufficiency",
            "Sufficient",
            grades.Where(g => g.DataSufficiency == DataSufficiency.Sufficient).ToList()));

        rows.Add(DecisionRow("Week", "W1-6", grades.Where(g => g.Week <= 6).ToList()));
        rows.Add(DecisionRow("Week", "W7-12", grades.Where(g => g.Week is >= 7 and <= 12).ToList()));
        rows.Add(DecisionRow("Week", "W13+", grades.Where(g => g.Week >= 13).ToList()));

        rows.Add(DecisionRow(
            "HistoryDepth",
            "1-2 prior weeks",
            grades.Where(g => g.ProjectionSourceWeeks.Count is >= 1 and <= 2).ToList()));
        rows.Add(DecisionRow(
            "HistoryDepth",
            "3-5 prior weeks",
            grades.Where(g => g.ProjectionSourceWeeks.Count is >= 3 and <= 5).ToList()));
        rows.Add(DecisionRow(
            "HistoryDepth",
            "6+ prior weeks",
            grades.Where(g => g.ProjectionSourceWeeks.Count >= 6).ToList()));

        return rows.Where(r => r.DecisionCount > 0).ToList();
    }

    private static DecisionBreakdownRow DecisionRow(
        string dimension,
        string bucket,
        IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var graded = grades.Where(g => g.WasCorrect is not null).ToList();
        var correct = graded.Count(g => g.WasCorrect == true);
        var diffs = graded
            .Where(g => g.ActualDecisionDifferential is not null)
            .Select(g => g.ActualDecisionDifferential!.Value)
            .OrderBy(v => v)
            .ToList();

        return new DecisionBreakdownRow
        {
            Dimension = dimension,
            Bucket = bucket,
            DecisionCount = grades.Count,
            GradedCount = graded.Count,
            AccuracyPercent = graded.Count == 0 ? null : Math.Round(100.0 * correct / graded.Count, 1),
            AverageDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Average(), 2),
            MedianDecisionValue = diffs.Count == 0 ? null : Math.Round(Median(diffs), 2),
            TotalDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Sum(), 2),
            WorstDecisionCost = diffs.Count == 0 ? null : Math.Round(diffs.First(), 2),
            BestDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Last(), 2)
        };
    }

    private static IReadOnlyList<ConfidenceBucketStats> BuildConfidenceBuckets(
        IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var list = new List<ConfidenceBucketStats>();
        foreach (var (label, min, maxEx) in ConfidenceBucketDefs)
        {
            var bucket = grades.Where(g => g.Confidence >= min && g.Confidence < maxEx).ToList();
            var graded = bucket.Where(g => g.WasCorrect is not null).ToList();
            var correct = graded.Count(g => g.WasCorrect == true);
            var diffs = graded
                .Where(g => g.ActualDecisionDifferential is not null)
                .Select(g => g.ActualDecisionDifferential!.Value)
                .ToList();
            list.Add(new ConfidenceBucketStats
            {
                Label = label,
                MinInclusive = min,
                MaxExclusive = maxEx,
                DecisionCount = bucket.Count,
                GradedCount = graded.Count,
                CorrectCount = correct,
                ActualSuccessRatePercent = graded.Count == 0
                    ? null
                    : Math.Round(100.0 * correct / graded.Count, 1),
                AverageDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Average(), 2),
                AverageConfidence = bucket.Count == 0 ? null : Math.Round(bucket.Average(g => g.Confidence), 1)
            });
        }

        return list;
    }

    private static IReadOnlyList<StructuralFinding> BuildStructuralFindings(
        IReadOnlyList<SeasonBenchmarkSummary> summaries,
        IReadOnlyList<PlayerProjectionEvaluation> fair,
        IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var findings = new List<StructuralFinding>();

        var seasonsLosingToA = summaries
            .Where(s => s.CurrentModelMae is not null &&
                        s.BaselineAMae is not null &&
                        s.CurrentModelMae > s.BaselineAMae + 0.05)
            .Select(s => s.Season)
            .ToList();
        if (seasonsLosingToA.Count >= 2)
        {
            findings.Add(new StructuralFinding
            {
                Kind = StructuralFindingKind.ConfirmedStructuralProblem,
                Title = "Current projection model loses to simple recent-average baseline",
                Evidence =
                    $"Current MAE worse than Baseline A in {seasonsLosingToA.Count}/{summaries.Count} seasons " +
                    $"({string.Join(", ", seasonsLosingToA)}). Aggregate ΔMAE vs A is not a single-season fluke.",
                SeasonsObserved = seasonsLosingToA
            });
        }
        else if (seasonsLosingToA.Count == 1)
        {
            findings.Add(new StructuralFinding
            {
                Kind = StructuralFindingKind.SeasonSpecificAnomaly,
                Title = "Current model loses to Baseline A in only one sampled season",
                Evidence = $"Only season {seasonsLosingToA[0]} shows a clear MAE loss vs Baseline A in this sample.",
                SeasonsObserved = seasonsLosingToA
            });
        }

        var overProjSeasons = summaries
            .Where(s => s.Bias is < -2)
            .Select(s => s.Season)
            .ToList();
        if (overProjSeasons.Count >= 2)
        {
            var byPos = fair.GroupBy(p => p.Position)
                .Select(g => (Pos: g.Key, Bias: g.Average(p => p.SignedError)))
                .ToList();
            var allPositionsOver = byPos.Count(x => x.Bias < -2) >= 3;
            findings.Add(new StructuralFinding
            {
                Kind = StructuralFindingKind.ConfirmedStructuralProblem,
                Title = "Systematic over-projection bias",
                Evidence =
                    $"Mean signed bias < -2 in seasons [{string.Join(", ", overProjSeasons)}]. " +
                    (allPositionsOver
                        ? "Bias appears across most skill positions, not a single-position artifact."
                        : "Bias concentrated in a subset of positions — inspect bias breakdown."),
                SeasonsObserved = overProjSeasons
            });
        }

        var highPred = fair.Where(p => p.PredictedPoints >= 20).ToList();
        var midPred = fair.Where(p => p.PredictedPoints is >= 10 and < 20).ToList();
        if (highPred.Count >= 40 && midPred.Count >= 20 &&
            highPred.Average(p => p.AbsoluteError) > midPred.Average(p => p.AbsoluteError) + 2)
        {
            findings.Add(new StructuralFinding
            {
                Kind = StructuralFindingKind.ConfirmedStructuralProblem,
                Title = "High projected scorers have larger absolute errors",
                Evidence =
                    $"MAE for predicted>=20 is {highPred.Average(p => p.AbsoluteError):0.00} " +
                    $"vs {midPred.Average(p => p.AbsoluteError):0.00} for predicted 10-20 " +
                    $"(n={highPred.Count}/{midPred.Count}).",
                SeasonsObserved = fair.Select(p => p.Season).Distinct().OrderBy(s => s).ToList()
            });
        }

        var low = grades.Where(g => g.Confidence < 40 && g.WasCorrect is not null).ToList();
        var high = grades.Where(g => g.Confidence >= 60 && g.WasCorrect is not null).ToList();
        if (high.Count < 8)
        {
            findings.Add(new StructuralFinding
            {
                Kind = StructuralFindingKind.ConfirmedStructuralProblem,
                Title = "Confidence massed in low bands (not calibrated)",
                Evidence =
                    $"Almost no decisions reach 60%+ confidence (n={high.Count}). " +
                    $"Low-confidence band n={low.Count}, success={(low.Count == 0 ? 0 : 100.0 * low.Count(g => g.WasCorrect == true) / low.Count):0.0}%. " +
                    "Higher confidence does not form a usable calibration curve.",
                SeasonsObserved = grades.Select(g => g.Season).Distinct().OrderBy(s => s).ToList()
            });
        }
        else
        {
            var lowAcc = 100.0 * low.Count(g => g.WasCorrect == true) / Math.Max(1, low.Count);
            var highAcc = 100.0 * high.Count(g => g.WasCorrect == true) / high.Count;
            if (highAcc + 3 < lowAcc)
            {
                findings.Add(new StructuralFinding
                {
                    Kind = StructuralFindingKind.ConfirmedStructuralProblem,
                    Title = "Higher confidence does not improve decision success",
                    Evidence = $"Low-conf accuracy={lowAcc:0.0}% vs high-conf accuracy={highAcc:0.0}%.",
                    SeasonsObserved = grades.Select(g => g.Season).Distinct().OrderBy(s => s).ToList()
                });
            }
            else if (Math.Abs(highAcc - lowAcc) < 5)
            {
                findings.Add(new StructuralFinding
                {
                    Kind = StructuralFindingKind.PossibleProblem,
                    Title = "Confidence weakly related to decision success",
                    Evidence = $"Low-conf accuracy={lowAcc:0.0}% vs high-conf accuracy={highAcc:0.0}% (gap < 5 pts).",
                    SeasonsObserved = grades.Select(g => g.Season).Distinct().OrderBy(s => s).ToList()
                });
            }
        }

        // Positive decision value consistency
        var positiveValueSeasons = summaries.Where(s => s.TotalDecisionValue > 0).Select(s => s.Season).ToList();
        var negativeValueSeasons = summaries.Where(s => s.TotalDecisionValue < 0).Select(s => s.Season).ToList();
        if (positiveValueSeasons.Count >= 2 && negativeValueSeasons.Count == 0)
        {
            findings.Add(new StructuralFinding
            {
                Kind = StructuralFindingKind.PossibleProblem,
                Title = "Decision value positive across sampled seasons (accuracy modest)",
                Evidence =
                    $"Total decision value > 0 in all sampled seasons with data " +
                    $"([{string.Join(", ", positiveValueSeasons)}]). Accuracy alone understates usefulness; " +
                    "confirm on holdout before claiming structural edge.",
                SeasonsObserved = positiveValueSeasons
            });
        }
        else if (negativeValueSeasons.Count >= 2)
        {
            findings.Add(new StructuralFinding
            {
                Kind = StructuralFindingKind.ConfirmedStructuralProblem,
                Title = "Decision value negative in multiple seasons",
                Evidence = $"Negative total decision value in [{string.Join(", ", negativeValueSeasons)}].",
                SeasonsObserved = negativeValueSeasons
            });
        }

        findings.Add(new StructuralFinding
        {
            Kind = StructuralFindingKind.DataLimitation,
            Title = "Historical news and fantasy ownership unavailable",
            Evidence =
                "All seasons mark news archive and historical fantasy ownership UNAVAILABLE. " +
                "Missing context can reduce decision quality independently of projection math. " +
                "Injuries/depth/snaps remain PARTIAL.",
            SeasonsObserved = summaries.Select(s => s.Season).ToList()
        });

        // Detect 2018-only anomalies vs others when sample >= 2 other seasons
        var others = summaries.Where(s => s.Season != Frozen2018SeasonBenchmark.Season).ToList();
        var s2018 = summaries.FirstOrDefault(s => s.Season == Frozen2018SeasonBenchmark.Season);
        if (s2018 is not null && others.Count >= 2 && s2018.DecisionAccuracyPercent is not null)
        {
            var otherAcc = others
                .Where(s => s.DecisionAccuracyPercent is not null)
                .Select(s => s.DecisionAccuracyPercent!.Value)
                .ToList();
            if (otherAcc.Count >= 2)
            {
                var meanOther = otherAcc.Average();
                if (Math.Abs(s2018.DecisionAccuracyPercent.Value - meanOther) >= 12)
                {
                    findings.Add(new StructuralFinding
                    {
                        Kind = StructuralFindingKind.SeasonSpecificAnomaly,
                        Title = "2018 decision accuracy diverges from other sampled seasons",
                        Evidence =
                            $"2018 accuracy={s2018.DecisionAccuracyPercent:0.0}% vs other-season mean={meanOther:0.0}%. " +
                            "Treat 2018 as frozen benchmark, not the sole optimization target.",
                        SeasonsObserved = [2018]
                    });
                }
            }
        }

        return findings;
    }

    private static double? AvgOrNull(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 2);
    }

    private static double Median(IReadOnlyList<double> sorted)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }
}
