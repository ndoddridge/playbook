using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Calibration;

/// <summary>
/// Fits monotone empirical confidence calibration with leave-one-season-out on development data only.
/// Never accepts holdout season observations.
/// </summary>
public static class ConfidenceCalibrationFitter
{
    public sealed record FittedMapping(
        IReadOnlyList<int> BinStarts,
        IReadOnlyList<int> CalibratedRates);

    public sealed record SelectionResult(
        FittedMapping Frozen,
        double PooledRawEce,
        double PooledCalibratedEce,
        double PooledRawBrier,
        double PooledCalibratedBrier,
        IReadOnlyList<string> Summaries);

    /// <summary>Standard reporting buckets.</summary>
    public static readonly (string Label, int Min, int MaxEx)[] ReportBuckets =
    [
        ("0-20%", 0, 20),
        ("20-40%", 20, 40),
        ("40-60%", 40, 60),
        ("60-80%", 60, 80),
        ("80-100%", 80, 101)
    ];

    public static SelectionResult SelectAndFreeze(
        IReadOnlyList<ConfidenceCalibrationObservation> observations,
        IReadOnlyList<int> developmentSeasons,
        int holdoutSeason)
    {
        if (observations.Any(o => o.Season == holdoutSeason))
        {
            throw new InvalidOperationException(
                $"Holdout season {holdoutSeason} observations were supplied to the confidence fitter.");
        }

        var forbidden = observations.Select(o => o.Season).Distinct()
            .Where(s => !developmentSeasons.Contains(s)).ToList();
        if (forbidden.Count > 0)
        {
            throw new InvalidOperationException(
                $"Non-development seasons in confidence calibration data: {string.Join(',', forbidden)}");
        }

        var seasons = developmentSeasons.Where(s => observations.Any(o => o.Season == s)).ToList();
        if (seasons.Count < 2)
        {
            throw new InvalidOperationException("Need at least 2 development seasons for LOOCV.");
        }

        // Candidate raw bin grids (merged later if sparse).
        var grids = new[]
        {
            new[] { 0, 12, 20, 28, 36 },
            new[] { 0, 20, 40 },
            new[] { 0, 15, 25, 35 }
        };

        var bestGrid = grids[0];
        var bestMeanEce = double.MaxValue;
        var summaries = new List<string>();

        foreach (var grid in grids)
        {
            var foldEces = new List<double>();
            foreach (var valSeason in seasons)
            {
                var train = observations.Where(o => o.Season != valSeason).ToList();
                var val = observations.Where(o => o.Season == valSeason).ToList();
                var fit = Fit(train, grid);
                foldEces.Add(Ece(val, o => Apply(fit, o.RawConfidence)));
            }

            var mean = foldEces.Average();
            summaries.Add($"Grid [{string.Join(',', grid)}] LOOCV mean ECE={mean:0.000}");
            if (mean + 1e-9 < bestMeanEce)
            {
                bestMeanEce = mean;
                bestGrid = grid;
            }
        }

        var frozen = Fit(observations, bestGrid);

        // Pooled LOOCV metrics with selected grid.
        var rawOutcomes = new List<(double P, bool Y)>();
        var calOutcomes = new List<(double P, bool Y)>();
        foreach (var valSeason in seasons)
        {
            var train = observations.Where(o => o.Season != valSeason).ToList();
            var val = observations.Where(o => o.Season == valSeason).ToList();
            var fit = Fit(train, bestGrid);
            var rawEce = Ece(val, o => o.RawConfidence);
            var calEce = Ece(val, o => Apply(fit, o.RawConfidence));
            summaries.Add($"Fold val={valSeason}: ECE raw={rawEce:0.000} cal={calEce:0.000} n={val.Count}");
            foreach (var o in val)
            {
                rawOutcomes.Add((o.RawConfidence / 100.0, o.WasCorrect));
                calOutcomes.Add((Apply(fit, o.RawConfidence) / 100.0, o.WasCorrect));
            }
        }

        summaries.Insert(0, $"Selected grid [{string.Join(',', bestGrid)}] (lowest LOOCV ECE={bestMeanEce:0.000})");
        summaries.Add(
            $"Frozen refit rates: [{string.Join(',', frozen.BinStarts)}] → [{string.Join(',', frozen.CalibratedRates)}]");

        return new SelectionResult(
            frozen,
            EceFromPairs(rawOutcomes),
            EceFromPairs(calOutcomes),
            Brier(rawOutcomes),
            Brier(calOutcomes),
            summaries);
    }

    public static FittedMapping Fit(
        IReadOnlyList<ConfidenceCalibrationObservation> train,
        IReadOnlyList<int> binStarts)
    {
        var starts = binStarts.OrderBy(x => x).ToArray();
        var counts = new int[starts.Length];
        var correct = new int[starts.Length];

        foreach (var o in train)
        {
            var idx = BinIndex(starts, o.RawConfidence);
            counts[idx]++;
            if (o.WasCorrect)
            {
                correct[idx]++;
            }
        }

        // Merge bins with < minCount into the next bin; shrink sparse remnants toward global mean.
        const int minCount = 25;
        var global = train.Count == 0 ? 0.5 : train.Average(o => o.WasCorrect ? 1.0 : 0.0);
        var mergedStarts = new List<int>();
        var mergedRates = new List<double>();
        var i = 0;
        while (i < starts.Length)
        {
            var c = counts[i];
            var k = correct[i];
            var start = starts[i];
            var j = i;
            while (c < minCount && j + 1 < starts.Length)
            {
                j++;
                c += counts[j];
                k += correct[j];
            }

            var rate = c == 0 ? global : k / (double)c;
            if (c < minCount)
            {
                rate = (rate * 0.7) + (global * 0.3);
            }

            mergedStarts.Add(start);
            mergedRates.Add(100.0 * rate);
            i = j + 1;
        }

        // Calibrated value = empirical success rate of the raw bin (reliability mapping).
        // Do NOT force rates to increase with raw confidence: historical raw confidence can be
        // anti-correlated. Higher calibrated scores still mean higher expected success because
        // the score IS the estimated success rate.
        var rates = mergedRates
            .Select(r => (int)Math.Round(r, MidpointRounding.AwayFromZero))
            .Select(r => Math.Clamp(r, 1, 99))
            .ToArray();

        return new FittedMapping(mergedStarts, rates);
    }

    public static int Apply(FittedMapping fit, int rawConfidence)
    {
        var raw = Math.Clamp(rawConfidence, 0, 100);
        var idx = BinIndex(fit.BinStarts, raw);
        return Math.Clamp(fit.CalibratedRates[idx], 0, 100);
    }

    public static ConfidenceCalibrationMetrics Evaluate(
        string label,
        IReadOnlyList<ConfidenceCalibrationObservation> rows,
        Func<ConfidenceCalibrationObservation, int> confidenceSelector)
    {
        var graded = rows.ToList();
        var pairs = graded.Select(o => (P: confidenceSelector(o) / 100.0, Y: o.WasCorrect)).ToList();
        var buckets = new List<ConfidenceCalibrationBucketRow>();
        foreach (var (bucketLabel, min, maxEx) in ReportBuckets)
        {
            var inBucket = graded.Where(o =>
            {
                var c = confidenceSelector(o);
                return c >= min && c < maxEx;
            }).ToList();
            var success = inBucket.Count == 0
                ? (double?)null
                : 100.0 * inBucket.Count(o => o.WasCorrect) / inBucket.Count;
            var avgConf = inBucket.Count == 0 ? (double?)null : inBucket.Average(o => confidenceSelector(o));
            var diffs = inBucket.Where(o => o.DecisionDifferential is not null)
                .Select(o => o.DecisionDifferential!.Value).ToList();
            var mid = (min + Math.Min(maxEx, 100)) / 2.0;
            buckets.Add(new ConfidenceCalibrationBucketRow
            {
                Label = bucketLabel,
                MinInclusive = min,
                MaxExclusive = maxEx,
                Count = inBucket.Count,
                AverageConfidence = avgConf is null ? null : Math.Round(avgConf.Value, 1),
                ActualSuccessRatePercent = success is null ? null : Math.Round(success.Value, 1),
                AverageDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Average(), 2),
                TotalDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Sum(), 2),
                AbsoluteCalibrationGap = success is null
                    ? null
                    : Math.Round(Math.Abs(success.Value - mid), 1)
            });
        }

        var populated = buckets.Where(b => b.Count >= 8 && b.ActualSuccessRatePercent is not null).ToList();
        var monotonic = true;
        for (var i = 1; i < populated.Count; i++)
        {
            if (populated[i].ActualSuccessRatePercent! + 0.5 < populated[i - 1].ActualSuccessRatePercent!)
            {
                monotonic = false;
                break;
            }
        }

        return new ConfidenceCalibrationMetrics
        {
            Label = label,
            GradedDecisions = graded.Count,
            Ece = pairs.Count == 0 ? null : Math.Round(EceFromPairs(pairs), 4),
            BrierScore = pairs.Count == 0 ? null : Math.Round(Brier(pairs), 4),
            OrderingGapPp = OrderingGap(graded, confidenceSelector),
            IsMonotonicByBucket = monotonic,
            Buckets = buckets
        };
    }

    private static double? OrderingGap(
        IReadOnlyList<ConfidenceCalibrationObservation> rows,
        Func<ConfidenceCalibrationObservation, int> conf)
    {
        if (rows.Count < 20)
        {
            return null;
        }

        var ordered = rows.OrderBy(conf).ToList();
        var mid = ordered.Count / 2;
        var low = ordered.Take(mid).ToList();
        var high = ordered.Skip(mid).ToList();
        if (low.Count == 0 || high.Count == 0)
        {
            return null;
        }

        var lowRate = 100.0 * low.Count(o => o.WasCorrect) / low.Count;
        var highRate = 100.0 * high.Count(o => o.WasCorrect) / high.Count;
        return Math.Round(highRate - lowRate, 1);
    }

    private static int BinIndex(IReadOnlyList<int> starts, int raw)
    {
        var idx = 0;
        for (var i = 0; i < starts.Count; i++)
        {
            if (starts[i] <= raw)
            {
                idx = i;
            }
        }

        return idx;
    }

    private static double Ece(
        IReadOnlyList<ConfidenceCalibrationObservation> rows,
        Func<ConfidenceCalibrationObservation, int> conf) =>
        EceFromPairs(rows.Select(o => (conf(o) / 100.0, o.WasCorrect)));

    private static double EceFromPairs(IEnumerable<(double P, bool Y)> pairs)
    {
        var list = pairs.ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        // 10 equal-width probability bins.
        double ece = 0;
        for (var b = 0; b < 10; b++)
        {
            var lo = b / 10.0;
            var hi = (b + 1) / 10.0;
            var inBin = list.Where(x => b == 9 ? x.P >= lo && x.P <= hi : x.P >= lo && x.P < hi).ToList();
            if (inBin.Count == 0)
            {
                continue;
            }

            var conf = inBin.Average(x => x.P);
            var acc = inBin.Average(x => x.Y ? 1.0 : 0.0);
            ece += (inBin.Count / (double)list.Count) * Math.Abs(acc - conf);
        }

        return ece;
    }

    private static double Brier(IEnumerable<(double P, bool Y)> pairs)
    {
        var list = pairs.ToList();
        return list.Count == 0 ? 0 : list.Average(x => Math.Pow(x.P - (x.Y ? 1.0 : 0.0), 2));
    }
}
