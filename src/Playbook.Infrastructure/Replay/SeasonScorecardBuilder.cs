using Playbook.Core.Players;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Aggregates week reports into a season scorecard.
/// Measurement only — does not tune models.
/// </summary>
public static class SeasonScorecardBuilder
{
    private static readonly (string Label, int Min, int MaxExclusive)[] ConfidenceBucketDefs =
    [
        ("0-20%", 0, 20),
        ("20-40%", 20, 40),
        ("40-60%", 40, 60),
        ("60-80%", 60, 80),
        ("80-100%", 80, 101)
    ];

    public static SeasonScorecard Build(
        MultiWeekReplayRequest request,
        IReadOnlyList<HistoricalReplayReport> weekReports,
        IReadOnlyList<WeekReplaySkip> skippedWeeks)
    {
        var projections = weekReports.SelectMany(w => w.ProjectionEvaluations).ToList();
        var grades = weekReports.SelectMany(w => w.Grades).ToList();
        var records = weekReports.SelectMany(w => w.DecisionRecords).ToList();

        // Fair common set: current model + both baselines + actual present.
        var fair = projections
            .Where(p =>
                p.BaselineRecentAbsoluteError is not null &&
                p.BaselineOpportunityAbsoluteError is not null)
            .ToList();

        double? modelMae = fair.Count == 0 ? null : Math.Round(fair.Average(p => p.AbsoluteError), 2);
        double? modelRmse = fair.Count == 0
            ? null
            : Math.Round(Math.Sqrt(fair.Average(p => p.SquaredError)), 2);
        double? modelBias = fair.Count == 0 ? null : Math.Round(fair.Average(p => p.SignedError), 2);
        double? maeA = fair.Count == 0
            ? null
            : Math.Round(fair.Average(p => p.BaselineRecentAbsoluteError!.Value), 2);
        double? maeB = fair.Count == 0
            ? null
            : Math.Round(fair.Average(p => p.BaselineOpportunityAbsoluteError!.Value), 2);

        string? better = null;
        if (modelMae is not null && maeA is not null && maeB is not null)
        {
            var ranked = new[]
            {
                ("Current model", modelMae.Value),
                ("Baseline A (recent average)", maeA.Value),
                ("Baseline B (opportunity-aware)", maeB.Value)
            }.OrderBy(x => x.Item2).ToList();
            better = ranked[0].Item2 == ranked[1].Item2
                ? $"Tie ({ranked[0].Item1} / {ranked[1].Item1})"
                : ranked[0].Item1;
        }

        var correct = grades.Count(g => g.WasCorrect == true);
        var incorrect = grades.Count(g => g.WasCorrect == false);
        var ungraded = grades.Count(g => g.WasCorrect is null);
        var graded = correct + incorrect;
        var diffs = grades
            .Where(g => g.ActualDecisionDifferential is not null)
            .Select(g => g.ActualDecisionDifferential!.Value)
            .OrderBy(v => v)
            .ToList();

        var weekCards = BuildWeekCards(request, weekReports, skippedWeeks);
        var buckets = BuildConfidenceBuckets(grades);
        var positions = BuildPositionCards(projections, grades, fair);
        var failures = BuildFailureLedger(grades);
        var patterns = BuildObservablePatterns(grades, fair);
        var quality = BuildDataQuality(request, weekReports, skippedWeeks, projections, grades);

        return new SeasonScorecard
        {
            Season = request.Season,
            StartWeek = request.StartWeek,
            EndWeek = request.EndWeek,
            ScoringType = request.ScoringType,
            FixtureId = request.FixtureId,
            GeneratedAt = DateTimeOffset.UtcNow,
            WeekReports = weekReports,
            SkippedWeeks = skippedWeeks,
            Weeks = weekCards,
            ProjectionEvaluations = projections,
            AllGrades = grades,
            AllDecisionRecords = records,
            FailureLedger = failures,
            ConfidenceBuckets = buckets,
            ByPosition = positions,
            ObservablePatterns = patterns,
            DataQuality = quality,
            FairProjectionCount = fair.Count,
            CurrentModelMae = modelMae,
            CurrentModelRmse = modelRmse,
            CurrentModelSignedBias = modelBias,
            BaselineAMae = maeA,
            BaselineBMae = maeB,
            BetterProjectionBaseline = better,
            TotalDecisions = grades.Count,
            CorrectDecisions = correct,
            IncorrectDecisions = incorrect,
            UngradedDecisions = ungraded,
            DecisionAccuracyPercent = graded == 0 ? null : Math.Round(100.0 * correct / graded, 1),
            AverageDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Average(), 2),
            MedianDecisionValue = diffs.Count == 0 ? null : Math.Round(Median(diffs), 2),
            TotalDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Sum(), 2),
            AverageConfidence = grades.Count == 0 ? 0 : Math.Round(grades.Average(g => g.Confidence), 1)
        };
    }

    private static IReadOnlyList<WeekScorecard> BuildWeekCards(
        MultiWeekReplayRequest request,
        IReadOnlyList<HistoricalReplayReport> weekReports,
        IReadOnlyList<WeekReplaySkip> skippedWeeks)
    {
        var byWeek = weekReports.ToDictionary(w => w.Week);
        var skipByWeek = skippedWeeks.ToDictionary(s => s.Week);
        var cards = new List<WeekScorecard>();
        for (var week = request.StartWeek; week <= request.EndWeek; week++)
        {
            if (byWeek.TryGetValue(week, out var report))
            {
                cards.Add(new WeekScorecard
                {
                    Week = week,
                    InformationCutoff = report.InformationCutoff,
                    Completed = true,
                    DecisionCount = report.DecisionCount,
                    CorrectCount = report.CorrectCount,
                    IncorrectCount = report.IncorrectCount,
                    DecisionAccuracyPercent = report.DecisionAccuracyPercent,
                    AverageDecisionValue = report.AverageDecisionDifferential,
                    ProjectionMae = report.AverageProjectionAbsoluteError,
                    BaselineAMae = report.BaselineRecentAverageMae,
                    BaselineBMae = report.BaselineOpportunityAwareMae,
                    AverageConfidence = report.AverageConfidence,
                    PlayersWithValidProjection = report.PlayersWithValidProjection,
                    PlayersEvaluated = report.PlayersEvaluated
                });
                continue;
            }

            skipByWeek.TryGetValue(week, out var skip);
            cards.Add(new WeekScorecard
            {
                Week = week,
                InformationCutoff = null,
                Completed = false,
                SkipReason = skip?.Reason ?? "Not completed",
                DecisionCount = 0,
                CorrectCount = 0,
                IncorrectCount = 0,
                DecisionAccuracyPercent = null,
                AverageDecisionValue = null,
                ProjectionMae = null,
                BaselineAMae = null,
                BaselineBMae = null,
                AverageConfidence = 0,
                PlayersWithValidProjection = 0,
                PlayersEvaluated = 0
            });
        }

        return cards;
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

    private static IReadOnlyList<PositionScorecard> BuildPositionCards(
        IReadOnlyList<PlayerProjectionEvaluation> projections,
        IReadOnlyList<ReplayDecisionGrade> grades,
        IReadOnlyList<PlayerProjectionEvaluation> fair)
    {
        var positions = new[] { Position.QB, Position.RB, Position.WR, Position.TE };
        var cards = new List<PositionScorecard>();
        foreach (var pos in positions)
        {
            var fairPos = fair.Where(p => p.Position == pos).ToList();
            var dec = grades.Where(g => g.Position == pos).ToList();
            var graded = dec.Where(g => g.WasCorrect is not null).ToList();
            var correct = graded.Count(g => g.WasCorrect == true);
            var diffs = graded
                .Where(g => g.ActualDecisionDifferential is not null)
                .Select(g => g.ActualDecisionDifferential!.Value)
                .ToList();

            cards.Add(new PositionScorecard
            {
                Position = pos,
                ProjectionCount = fairPos.Count,
                ProjectionMae = fairPos.Count == 0 ? null : Math.Round(fairPos.Average(p => p.AbsoluteError), 2),
                BaselineAMae = fairPos.Count == 0
                    ? null
                    : Math.Round(fairPos.Average(p => p.BaselineRecentAbsoluteError!.Value), 2),
                BaselineBMae = fairPos.Count == 0
                    ? null
                    : Math.Round(fairPos.Average(p => p.BaselineOpportunityAbsoluteError!.Value), 2),
                SignedBias = fairPos.Count == 0 ? null : Math.Round(fairPos.Average(p => p.SignedError), 2),
                DecisionCount = dec.Count,
                GradedDecisionCount = graded.Count,
                DecisionAccuracyPercent = graded.Count == 0
                    ? null
                    : Math.Round(100.0 * correct / graded.Count, 1),
                AverageDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Average(), 2)
            });
        }

        return cards;
    }

    private static IReadOnlyList<FailureLedgerEntry> BuildFailureLedger(
        IReadOnlyList<ReplayDecisionGrade> grades)
    {
        return grades
            .Where(g => g.WasCorrect == false)
            .Select(g => new FailureLedgerEntry
            {
                DecisionId = g.DecisionId,
                Season = g.Season,
                Week = g.Week,
                InformationCutoff = g.InformationCutoff,
                PlayerId = g.PlayerId,
                PlayerName = g.PlayerName,
                Position = g.Position,
                Recommendation = g.Recommendation,
                PredictedPoints = g.ExpectedValue,
                ActualPoints = g.ActualFantasyPoints,
                Confidence = g.Confidence,
                DataSufficiency = g.DataSufficiency,
                AlternativePlayerName = g.AlternativePlayerName,
                AlternativePredictedPoints = g.AlternativeExpectedValue,
                AlternativeActualPoints = g.AlternativeActualFantasyPoints,
                DecisionCost = g.ActualDecisionDifferential,
                EvaluationSummary = g.EvaluationSummary,
                SupportingEvidence = g.SupportingEvidence,
                OpposingEvidence = g.OpposingEvidence,
                Unknowns = g.Unknowns,
                Rationale = g.Rationale,
                ProjectionSourceWeeks = g.ProjectionSourceWeeks
            })
            .OrderBy(f => f.DecisionCost ?? 0)
            .ThenBy(f => f.Week)
            .ThenBy(f => f.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ObservablePattern> BuildObservablePatterns(
        IReadOnlyList<ReplayDecisionGrade> grades,
        IReadOnlyList<PlayerProjectionEvaluation> fair)
    {
        var patterns = new List<ObservablePattern>();
        const int minSample = 8;

        // Position accuracy extremes
        var posGroups = grades
            .Where(g => g.WasCorrect is not null)
            .GroupBy(g => g.Position)
            .Select(g => new
            {
                Position = g.Key,
                N = g.Count(),
                Acc = 100.0 * g.Count(x => x.WasCorrect == true) / g.Count()
            })
            .Where(x => x.N >= minSample)
            .OrderBy(x => x.Acc)
            .ToList();
        if (posGroups.Count >= 2)
        {
            var worst = posGroups.First();
            var best = posGroups.Last();
            patterns.Add(new ObservablePattern
            {
                Dimension = "Position",
                Bucket = worst.Position.ToString(),
                SampleSize = worst.N,
                MetricValue = Math.Round(worst.Acc, 1),
                MetricName = "DecisionAccuracyPercent",
                Notes = $"Lowest graded accuracy among positions with n>={minSample}; best={best.Position} ({best.Acc:0.0}%, n={best.N})."
            });
        }

        // Confidence: low vs high
        var lowConf = grades.Where(g => g.Confidence < 40 && g.WasCorrect is not null).ToList();
        var highConf = grades.Where(g => g.Confidence >= 60 && g.WasCorrect is not null).ToList();
        if (lowConf.Count >= minSample && highConf.Count >= minSample)
        {
            var lowAcc = 100.0 * lowConf.Count(g => g.WasCorrect == true) / lowConf.Count;
            var highAcc = 100.0 * highConf.Count(g => g.WasCorrect == true) / highConf.Count;
            patterns.Add(new ObservablePattern
            {
                Dimension = "Confidence",
                Bucket = "low(<40) vs high(>=60)",
                SampleSize = lowConf.Count + highConf.Count,
                MetricValue = Math.Round(highAcc - lowAcc, 1),
                MetricName = "AccuracyGapPercent",
                Notes =
                    $"Low-confidence accuracy={lowAcc:0.0}% (n={lowConf.Count}); " +
                    $"high-confidence accuracy={highAcc:0.0}% (n={highConf.Count})."
            });
        }

        // Data sufficiency
        var limited = grades.Where(g => g.DataSufficiency == DataSufficiency.Limited && g.WasCorrect is not null).ToList();
        var sufficient = grades.Where(g => g.DataSufficiency == DataSufficiency.Sufficient && g.WasCorrect is not null).ToList();
        if (limited.Count >= minSample && sufficient.Count >= minSample)
        {
            var limAcc = 100.0 * limited.Count(g => g.WasCorrect == true) / limited.Count;
            var sufAcc = 100.0 * sufficient.Count(g => g.WasCorrect == true) / sufficient.Count;
            patterns.Add(new ObservablePattern
            {
                Dimension = "DataSufficiency",
                Bucket = "Limited vs Sufficient",
                SampleSize = limited.Count + sufficient.Count,
                MetricValue = Math.Round(sufAcc - limAcc, 1),
                MetricName = "AccuracyGapPercent",
                Notes =
                    $"Limited accuracy={limAcc:0.0}% (n={limited.Count}); " +
                    $"Sufficient accuracy={sufAcc:0.0}% (n={sufficient.Count})."
            });
        }

        // Recommendation margin
        var weak = grades.Where(g => g.RecommendationMargin is < 3 && g.WasCorrect is not null).ToList();
        var strong = grades.Where(g => g.RecommendationMargin is >= 6 && g.WasCorrect is not null).ToList();
        if (weak.Count >= minSample && strong.Count >= minSample)
        {
            var weakAcc = 100.0 * weak.Count(g => g.WasCorrect == true) / weak.Count;
            var strongAcc = 100.0 * strong.Count(g => g.WasCorrect == true) / strong.Count;
            patterns.Add(new ObservablePattern
            {
                Dimension = "RecommendationMargin",
                Bucket = "weak(<3) vs strong(>=6)",
                SampleSize = weak.Count + strong.Count,
                MetricValue = Math.Round(strongAcc - weakAcc, 1),
                MetricName = "AccuracyGapPercent",
                Notes =
                    $"Weak-margin accuracy={weakAcc:0.0}% (n={weak.Count}); " +
                    $"strong-margin accuracy={strongAcc:0.0}% (n={strong.Count})."
            });
        }

        // Projection bias by position
        foreach (var group in fair.GroupBy(p => p.Position).Where(g => g.Count() >= minSample))
        {
            var bias = group.Average(p => p.SignedError);
            if (Math.Abs(bias) < 1.0)
            {
                continue;
            }

            patterns.Add(new ObservablePattern
            {
                Dimension = "ProjectionBias",
                Bucket = group.Key.ToString(),
                SampleSize = group.Count(),
                MetricValue = Math.Round(bias, 2),
                MetricName = "SignedErrorActualMinusPredicted",
                Notes = bias > 0
                    ? "Observable under-projection on average (actual > predicted)."
                    : "Observable over-projection on average (actual < predicted)."
            });
        }

        // Early vs late season decision accuracy
        var early = grades.Where(g => g.Week <= 6 && g.WasCorrect is not null).ToList();
        var late = grades.Where(g => g.Week >= 12 && g.WasCorrect is not null).ToList();
        if (early.Count >= minSample && late.Count >= minSample)
        {
            var earlyAcc = 100.0 * early.Count(g => g.WasCorrect == true) / early.Count;
            var lateAcc = 100.0 * late.Count(g => g.WasCorrect == true) / late.Count;
            patterns.Add(new ObservablePattern
            {
                Dimension = "SeasonPhase",
                Bucket = "W1-6 vs W12+",
                SampleSize = early.Count + late.Count,
                MetricValue = Math.Round(lateAcc - earlyAcc, 1),
                MetricName = "AccuracyGapPercent",
                Notes =
                    $"Early accuracy={earlyAcc:0.0}% (n={early.Count}); " +
                    $"late accuracy={lateAcc:0.0}% (n={late.Count})."
            });
        }

        // High projected scorers vs mid
        var highProj = fair.Where(p => p.PredictedPoints >= 20).ToList();
        var midProj = fair.Where(p => p.PredictedPoints is >= 10 and < 20).ToList();
        if (highProj.Count >= minSample && midProj.Count >= minSample)
        {
            patterns.Add(new ObservablePattern
            {
                Dimension = "ProjectionSize",
                Bucket = "high(>=20) vs mid(10-20)",
                SampleSize = highProj.Count + midProj.Count,
                MetricValue = Math.Round(highProj.Average(p => p.AbsoluteError) - midProj.Average(p => p.AbsoluteError), 2),
                MetricName = "MaeGap",
                Notes =
                    $"High-projection MAE={highProj.Average(p => p.AbsoluteError):0.00} (n={highProj.Count}); " +
                    $"mid-projection MAE={midProj.Average(p => p.AbsoluteError):0.00} (n={midProj.Count})."
            });
        }

        return patterns;
    }

    private static HistoricalDataQualityReport BuildDataQuality(
        MultiWeekReplayRequest request,
        IReadOnlyList<HistoricalReplayReport> weekReports,
        IReadOnlyList<WeekReplaySkip> skippedWeeks,
        IReadOnlyList<PlayerProjectionEvaluation> projections,
        IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var weeksRequested = request.EndWeek - request.StartWeek + 1;
        var playersEvaluated = weekReports.Sum(w => w.PlayersEvaluated);
        var validProj = weekReports.Sum(w => w.PlayersWithValidProjection);
        var injury = weekReports.Sum(w => w.PlayersWithInjurySignal);
        var usage = weekReports.Sum(w => w.PlayersWithUsageSignal);
        var role = weekReports.Sum(w => w.PlayersWithRoleSignal);

        var sufficiencySource = weekReports
            .SelectMany(w => w.ProjectionEvaluations)
            .Select(p => p.DataSufficiency)
            .Concat(weekReports.SelectMany(w => w.Grades).Select(g => g.DataSufficiency))
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .ToList();

        // Also count players without valid projection as insufficient when present on roster.
        var rosterPlayers = weekReports.Sum(w => w.PlayersEvaluated);
        var insufficientDecisions = grades.Count(g => g.DataSufficiency == DataSufficiency.Insufficient);

        double Pct(int num, int den) => den == 0 ? 0 : Math.Round(100.0 * num / den, 1);

        var unavailable = weekReports
            .SelectMany(w => w.UnavailableSources)
            .Select(NormalizeUnavailable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HistoricalDataQualityReport
        {
            WeeksRequested = weeksRequested,
            WeeksCompleted = weekReports.Count,
            WeeksSkipped = skippedWeeks.Count,
            PlayersEvaluated = playersEvaluated,
            PlayersWithValidProjection = validProj,
            PercentPlayersWithValidProjection = Pct(validProj, Math.Max(1, playersEvaluated)),
            DecisionsGenerated = grades.Count,
            DecisionsGraded = grades.Count(g => g.WasCorrect is not null),
            DecisionsSkippedInsufficientData = insufficientDecisions,
            ProjectionEvaluations = projections.Count,
            PercentWithInjurySignal = Pct(injury, Math.Max(1, rosterPlayers)),
            PercentWithUsageSignal = Pct(usage, Math.Max(1, rosterPlayers)),
            PercentWithRoleSignal = Pct(role, Math.Max(1, rosterPlayers)),
            PercentSufficientHistory = Pct(
                sufficiencySource.Count(s => s == DataSufficiency.Sufficient),
                Math.Max(1, sufficiencySource.Count)),
            PercentLimitedHistory = Pct(
                sufficiencySource.Count(s => s == DataSufficiency.Limited),
                Math.Max(1, sufficiencySource.Count)),
            PercentInsufficientHistory = Pct(
                sufficiencySource.Count(s => s == DataSufficiency.Insufficient),
                Math.Max(1, sufficiencySource.Count)),
            UnavailableInformation = unavailable,
            SkippedWeeks = skippedWeeks
        };
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

    private static string NormalizeUnavailable(string source)
    {
        // Collapse per-week ownership/depth/snap notices into stable domain labels.
        if (source.Contains("Historical fantasy league ownership", StringComparison.OrdinalIgnoreCase))
        {
            return "Historical fantasy league ownership: UNAVAILABLE — reconstructed lab roster (not historical ownership)";
        }

        if (source.Contains("depth charts", StringComparison.OrdinalIgnoreCase))
        {
            return "Depth charts: PARTIAL — prior-week depth used (no trustworthy same-week cutoff)";
        }

        if (source.Contains("snap counts", StringComparison.OrdinalIgnoreCase))
        {
            return "Snap counts: PARTIAL — prior weeks only (target-week snaps excluded)";
        }

        return source;
    }
}
