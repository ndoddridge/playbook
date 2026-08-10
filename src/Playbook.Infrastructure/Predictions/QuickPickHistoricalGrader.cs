using Playbook.Core.Knowledge;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Attaches Week W outcomes after predictions are finalized and grades them.
/// Outcomes never influence prediction generation.
/// </summary>
public static class QuickPickHistoricalGrader
{
    public static IReadOnlyList<QuickPickGradedPrediction> Grade(
        IReadOnlyList<QuickPickHistoricalPrediction> predictions,
        HistoricalWeekOutcomes outcomes)
    {
        // Actual ranks within market (among players we predicted that have actuals).
        var actualByKey = new Dictionary<(PredictionMarketType Market, Guid PlayerId), double>();
        foreach (var p in predictions)
        {
            if (!outcomes.ByPlayerId.TryGetValue(p.PlayerId, out var outcome))
            {
                continue;
            }

            var actual = ResolveActual(outcome, p.Market);
            if (actual is null)
            {
                continue;
            }

            actualByKey[(p.Market, p.PlayerId)] = actual.Value;
        }

        var actualRank = new Dictionary<(PredictionMarketType Market, Guid PlayerId), int>();
        foreach (var marketGroup in actualByKey.GroupBy(kv => kv.Key.Market))
        {
            var ordered = marketGroup
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key.PlayerId)
                .ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                actualRank[(marketGroup.Key, ordered[i].Key.PlayerId)] = i + 1;
            }
        }

        var graded = new List<QuickPickGradedPrediction>();
        foreach (var p in predictions)
        {
            if (!actualByKey.TryGetValue((p.Market, p.PlayerId), out var actual))
            {
                continue;
            }

            var aRank = actualRank[(p.Market, p.PlayerId)];
            var marketSize = actualByKey.Count(kv => kv.Key.Market == p.Market);
            var top5Ceiling = Math.Min(FrozenQuickPicksHistoricalEvaluationV1.TopNSmall, marketSize);
            var top10Ceiling = Math.Min(FrozenQuickPicksHistoricalEvaluationV1.TopNLarge, marketSize);

            var inProjTop5 = p.RankInMarket <= top5Ceiling;
            var inActTop5 = aRank <= top5Ceiling;
            var inProjTop10 = p.RankInMarket <= top10Ceiling;
            var inActTop10 = aRank <= top10Ceiling;

            graded.Add(new QuickPickGradedPrediction
            {
                Prediction = p,
                ActualValue = actual,
                ActualRankInMarket = aRank,
                AbsoluteError = QuickPickHistoricalGrading.AbsoluteError(p.ProjectedValue, actual),
                SignedError = QuickPickHistoricalGrading.SignedError(p.ProjectedValue, actual),
                RankAbsoluteError = Math.Abs(p.RankInMarket - aRank),
                InProjectedTop5 = inProjTop5,
                InActualTop5 = inActTop5,
                Top5Hit = QuickPickHistoricalGrading.TopNHit(inProjTop5, inActTop5),
                InProjectedTop10 = inProjTop10,
                InActualTop10 = inActTop10,
                Top10Hit = QuickPickHistoricalGrading.TopNHit(inProjTop10, inActTop10)
            });
        }

        return graded
            .OrderBy(g => g.Prediction.Market)
            .ThenBy(g => g.Prediction.RankInMarket)
            .ThenBy(g => g.Prediction.PlayerId)
            .ToList();
    }

    public static QuickPickSeasonScorecard BuildScorecard(
        int season,
        QuickPickMode mode,
        KnowledgeImpactGroup activeGroups,
        IReadOnlyList<QuickPickGradedPrediction> graded)
    {
        var weeks = graded.Select(g => g.Prediction.Week).Distinct().Count();
        var n = graded.Count;
        if (n == 0)
        {
            return new QuickPickSeasonScorecard
            {
                Season = season,
                Mode = mode,
                ActiveGroups = activeGroups,
                WeeksEvaluated = 0,
                PredictionsEvaluated = 0,
                MeanAbsoluteError = 0,
                MeanBias = 0,
                Top5HitRate = 0,
                Top10HitRate = 0,
                MeanRankAbsoluteError = 0,
                AverageConfidence = null,
                TotalPredictionValue = 0,
                Graded = graded,
                EvaluatorVersion = FrozenQuickPicksHistoricalEvaluationV1.EvaluatorVersion
            };
        }

        var projectedTop5 = graded.Where(g => g.InProjectedTop5).ToList();
        var projectedTop10 = graded.Where(g => g.InProjectedTop10).ToList();
        var confs = graded.Select(g => g.Prediction.Confidence).Where(c => c is not null).Select(c => (double)c!).ToList();

        return new QuickPickSeasonScorecard
        {
            Season = season,
            Mode = mode,
            ActiveGroups = activeGroups,
            WeeksEvaluated = weeks,
            PredictionsEvaluated = n,
            MeanAbsoluteError = graded.Average(g => g.AbsoluteError),
            MeanBias = graded.Average(g => g.SignedError),
            Top5HitRate = projectedTop5.Count == 0
                ? 0
                : 100.0 * projectedTop5.Count(g => g.Top5Hit) / projectedTop5.Count,
            Top10HitRate = projectedTop10.Count == 0
                ? 0
                : 100.0 * projectedTop10.Count(g => g.Top10Hit) / projectedTop10.Count,
            MeanRankAbsoluteError = graded.Average(g => g.RankAbsoluteError),
            AverageConfidence = confs.Count == 0 ? null : confs.Average(),
            TotalPredictionValue = QuickPickHistoricalGrading.TotalPredictionValue(
                graded.Select(g => g.AbsoluteError)),
            Graded = graded,
            EvaluatorVersion = FrozenQuickPicksHistoricalEvaluationV1.EvaluatorVersion
        };
    }

    public static QuickPickChangeAnalysis AnalyzeChanges(
        IReadOnlyList<QuickPickSeasonScorecard> baselineCards,
        IReadOnlyList<QuickPickSeasonScorecard> enhancedCards)
    {
        var baseline = baselineCards.SelectMany(c => c.Graded).ToDictionary(
            g => Key(g),
            g => g);
        var enhanced = enhancedCards.SelectMany(c => c.Graded).ToDictionary(
            g => Key(g),
            g => g);

        var keys = baseline.Keys.Intersect(enhanced.Keys).OrderBy(k => k).ToList();
        var changed = new List<QuickPickChangeRecord>();
        var helped = new List<QuickPickChangeRecord>();
        var hurt = new List<QuickPickChangeRecord>();
        var neutral = new List<QuickPickChangeRecord>();

        foreach (var key in keys)
        {
            var b = baseline[key];
            var e = enhanced[key];
            var magnitude = Math.Abs(e.Prediction.RankingScore - b.Prediction.RankingScore)
                            + Math.Abs(e.Prediction.ProjectedValue - b.Prediction.ProjectedValue)
                            + Math.Abs(e.Prediction.RankInMarket - b.Prediction.RankInMarket);

            var ledger = QuickPickHistoricalGrading.ClassifyLedger(
                b.RankAbsoluteError, e.RankAbsoluteError, magnitude);

            var form = e.Prediction.KnowledgeContext?.Knowledge.Evidence
                .FirstOrDefault(ev =>
                    ev.Aspect == KnowledgeAspect.RecentProduction && !ev.IsUnavailableMarker);

            var record = new QuickPickChangeRecord
            {
                Season = b.Prediction.Season,
                Week = b.Prediction.Week,
                PlayerId = b.Prediction.PlayerId,
                PlayerName = b.Prediction.PlayerName,
                Position = b.Prediction.Position,
                Market = b.Prediction.Market,
                BaselineProjectedValue = b.Prediction.ProjectedValue,
                EnhancedProjectedValue = e.Prediction.ProjectedValue,
                BaselineRankingScore = b.Prediction.RankingScore,
                EnhancedRankingScore = e.Prediction.RankingScore,
                BaselineRank = b.Prediction.RankInMarket,
                EnhancedRank = e.Prediction.RankInMarket,
                Magnitude = magnitude,
                BaselineAbsoluteError = b.AbsoluteError,
                EnhancedAbsoluteError = e.AbsoluteError,
                BaselineRankError = b.RankAbsoluteError,
                EnhancedRankError = e.RankAbsoluteError,
                LedgerClass = ledger,
                ActualValue = e.ActualValue,
                Confidence = e.Prediction.Confidence,
                KnowledgeConfidence = e.Prediction.KnowledgeContext?.Knowledge.KnowledgeConfidence,
                RecentFormValue = form?.Value,
                RecentFormStatement = form?.Statement,
                RecentFormDirection = form?.Direction.ToString()
            };

            if (magnitude > 1e-9)
            {
                changed.Add(record);
            }

            switch (ledger)
            {
                case "HELPED":
                    helped.Add(record);
                    break;
                case "HURT":
                    hurt.Add(record);
                    break;
                default:
                    neutral.Add(record);
                    break;
            }
        }

        var unchanged = keys.Count - changed.Count;
        var ranksChanged = changed.Count(c => c.BaselineRank != c.EnhancedRank);
        var baseMae = keys.Count == 0 ? 0 : keys.Average(k => baseline[k].AbsoluteError);
        var enhMae = keys.Count == 0 ? 0 : keys.Average(k => enhanced[k].AbsoluteError);
        var baseTop5 = HitRate(keys.Select(k => baseline[k]).Where(g => g.InProjectedTop5).ToList());
        var enhTop5 = HitRate(keys.Select(k => enhanced[k]).Where(g => g.InProjectedTop5).ToList());
        var baseVal = QuickPickHistoricalGrading.TotalPredictionValue(
            keys.Select(k => baseline[k].AbsoluteError));
        var enhVal = QuickPickHistoricalGrading.TotalPredictionValue(
            keys.Select(k => enhanced[k].AbsoluteError));

        return new QuickPickChangeAnalysis
        {
            PredictionsCompared = keys.Count,
            PredictionsChanged = changed.Count,
            PredictionsUnchanged = unchanged,
            RanksChanged = ranksChanged,
            PercentChanged = keys.Count == 0 ? 0 : 100.0 * changed.Count / keys.Count,
            PercentRanksChanged = keys.Count == 0 ? 0 : 100.0 * ranksChanged / keys.Count,
            AverageMagnitudeOfChange = changed.Count == 0 ? 0 : changed.Average(c => c.Magnitude),
            BaselineMeanAbsoluteError = baseMae,
            EnhancedMeanAbsoluteError = enhMae,
            BaselineTop5HitRate = baseTop5,
            EnhancedTop5HitRate = enhTop5,
            BaselineTotalPredictionValue = baseVal,
            EnhancedTotalPredictionValue = enhVal,
            PredictionsIdentical = changed.Count == 0,
            Changed = changed
                .OrderByDescending(c => c.Magnitude)
                .ThenBy(c => c.PlayerName, StringComparer.Ordinal)
                .ToList(),
            Helped = helped,
            Hurt = hurt,
            Neutral = neutral,
            BestChanges = helped
                .OrderBy(c => c.EnhancedRankError - c.BaselineRankError)
                .ThenByDescending(c => c.Magnitude)
                .Take(10)
                .ToList(),
            WorstChanges = hurt
                .OrderByDescending(c => c.EnhancedRankError - c.BaselineRankError)
                .ThenByDescending(c => c.Magnitude)
                .Take(10)
                .ToList()
        };
    }

    private static double HitRate(IReadOnlyList<QuickPickGradedPrediction> topN) =>
        topN.Count == 0 ? 0 : 100.0 * topN.Count(g => g.Top5Hit) / topN.Count;

    private static string Key(QuickPickGradedPrediction g) =>
        $"{g.Prediction.Season}|{g.Prediction.Week}|{g.Prediction.PlayerId:N}|{g.Prediction.Market}";

    public static double? ResolveActual(HistoricalPlayerOutcome outcome, PredictionMarketType market) =>
        market switch
        {
            PredictionMarketType.PassingYards => outcome.ActualPassYards,
            PredictionMarketType.RushingYards => outcome.ActualRushYards,
            PredictionMarketType.ReceivingYards => outcome.ActualReceivingYards,
            PredictionMarketType.Receptions => outcome.ActualReceptions,
            PredictionMarketType.PassingTouchdowns => outcome.ActualPassTouchdowns,
            _ => null
        };
}
