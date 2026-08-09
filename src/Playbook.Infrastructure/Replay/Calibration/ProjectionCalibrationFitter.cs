using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Calibration;

/// <summary>
/// Fits simple calibration candidates with leave-one-season-out on development data only.
/// Never accepts holdout season observations.
/// </summary>
public static class ProjectionCalibrationFitter
{
    public sealed record FittedCalibration(
        ProjectionCalibrationMethod Method,
        double LowIntercept,
        double LowSlope,
        double HighIntercept,
        double HighSlope,
        double Threshold);

    public sealed record FoldResult(
        int ValidateSeason,
        FittedCalibration Calibration,
        double MaeV1,
        double MaeV2,
        double BiasV1,
        double BiasV2,
        double MaeBaselineA);

    public sealed record SelectionResult(
        FittedCalibration Frozen,
        IReadOnlyList<FoldResult> Folds,
        ProjectionCalibrationMethod SelectedMethod,
        double PooledDevMaeV1,
        double PooledDevMaeV2,
        double PooledDevBiasV1,
        double PooledDevBiasV2,
        double PooledDevMaeBaselineA,
        IReadOnlyList<string> Summaries);

    public static SelectionResult SelectAndFreeze(
        IReadOnlyList<CalibrationObservation> observations,
        IReadOnlyList<int> developmentSeasons,
        int holdoutSeason)
    {
        if (observations.Any(o => o.Season == holdoutSeason))
        {
            throw new InvalidOperationException(
                $"Holdout season {holdoutSeason} observations were supplied to the calibration fitter.");
        }

        var forbidden = observations.Select(o => o.Season).Distinct()
            .Where(s => !developmentSeasons.Contains(s))
            .ToList();
        if (forbidden.Count > 0)
        {
            throw new InvalidOperationException(
                $"Non-development seasons in calibration data: {string.Join(',', forbidden)}");
        }

        var seasons = developmentSeasons.Where(s => observations.Any(o => o.Season == s)).ToList();
        if (seasons.Count < 2)
        {
            throw new InvalidOperationException("Need at least 2 development seasons for LOOCV.");
        }

        var methods = new[]
        {
            ProjectionCalibrationMethod.GlobalScale,
            ProjectionCalibrationMethod.GlobalAffine,
            ProjectionCalibrationMethod.PiecewiseScaleAt20,
            ProjectionCalibrationMethod.PiecewiseAffineAt20
        };

        // Mean validation MAE across folds for each method.
        var methodScores = new Dictionary<ProjectionCalibrationMethod, List<double>>();
        var methodBias = new Dictionary<ProjectionCalibrationMethod, List<double>>();
        foreach (var method in methods)
        {
            methodScores[method] = [];
            methodBias[method] = [];
        }

        var foldDetails = new List<(int ValSeason, ProjectionCalibrationMethod Method, FittedCalibration Fit, double MaeV1, double MaeV2, double BiasV1, double BiasV2, double MaeA)>();

        foreach (var valSeason in seasons)
        {
            var train = observations.Where(o => o.Season != valSeason).ToList();
            var val = observations.Where(o => o.Season == valSeason).ToList();
            var maeV1 = Mae(val, o => o.V1Predicted);
            var biasV1 = Bias(val, o => o.V1Predicted);
            var maeA = Mae(val, o => o.BaselineAPredicted);

            foreach (var method in methods)
            {
                var fit = Fit(method, train, threshold: 20);
                var maeV2 = Mae(val, o => Apply(fit, o.V1Predicted));
                var biasV2 = Bias(val, o => Apply(fit, o.V1Predicted));
                methodScores[method].Add(maeV2);
                methodBias[method].Add(Math.Abs(biasV2));
                foldDetails.Add((valSeason, method, fit, maeV1, maeV2, biasV1, biasV2, maeA));
            }
        }

        var ranked = methods
            .Select(m => new
            {
                Method = m,
                MeanMae = methodScores[m].Average(),
                MeanAbsBias = methodBias[m].Average()
            })
            .OrderBy(x => x.MeanMae)
            .ThenBy(x => x.MeanAbsBias)
            .ToList();

        var selected = ranked[0].Method;
        var frozen = Fit(selected, observations, threshold: 20);

        // Pooled LOOCV predictions: for each season, use model trained on other seasons.
        var looPreds = new List<(double V1, double V2, double A, double Y)>();
        var folds = new List<FoldResult>();
        var summaries = new List<string>();
        foreach (var valSeason in seasons)
        {
            var train = observations.Where(o => o.Season != valSeason).ToList();
            var val = observations.Where(o => o.Season == valSeason).ToList();
            var fit = Fit(selected, train, threshold: 20);
            var maeV1 = Mae(val, o => o.V1Predicted);
            var maeV2 = Mae(val, o => Apply(fit, o.V1Predicted));
            var biasV1 = Bias(val, o => o.V1Predicted);
            var biasV2 = Bias(val, o => Apply(fit, o.V1Predicted));
            var maeA = Mae(val, o => o.BaselineAPredicted);
            folds.Add(new FoldResult(valSeason, fit, maeV1, maeV2, biasV1, biasV2, maeA));
            summaries.Add(
                $"Fold val={valSeason}: MAE V1={maeV1:0.00} V2={maeV2:0.00} A={maeA:0.00}; " +
                $"bias V1={biasV1:0.00} V2={biasV2:0.00}");
            foreach (var o in val)
            {
                looPreds.Add((o.V1Predicted, Apply(fit, o.V1Predicted), o.BaselineAPredicted, o.Actual));
            }
        }

        summaries.Insert(0,
            $"LOOCV method ranking: {string.Join(" < ", ranked.Select(r => $"{r.Method}(mae={r.MeanMae:0.00})"))}");
        summaries.Add(
            $"Frozen refit on all development: {frozen.Method} " +
            $"low=({frozen.LowIntercept:0.####},{frozen.LowSlope:0.####}) " +
            $"high=({frozen.HighIntercept:0.####},{frozen.HighSlope:0.####}) thr={frozen.Threshold}");

        return new SelectionResult(
            frozen,
            folds,
            selected,
            MaePairs(looPreds.Select(p => (p.V1, p.Y))),
            MaePairs(looPreds.Select(p => (p.V2, p.Y))),
            BiasPairs(looPreds.Select(p => (p.V1, p.Y))),
            BiasPairs(looPreds.Select(p => (p.V2, p.Y))),
            MaePairs(looPreds.Select(p => (p.A, p.Y))),
            summaries);
    }

    public static FittedCalibration Fit(
        ProjectionCalibrationMethod method,
        IReadOnlyList<CalibrationObservation> train,
        double threshold)
    {
        return method switch
        {
            ProjectionCalibrationMethod.Identity =>
                new FittedCalibration(method, 0, 1, 0, 1, threshold),
            ProjectionCalibrationMethod.GlobalScale =>
                FitScale(train, method, threshold),
            ProjectionCalibrationMethod.GlobalAffine =>
                FitAffine(train, method, threshold, piecewise: false),
            ProjectionCalibrationMethod.PiecewiseScaleAt20 =>
                FitPiecewiseScale(train, threshold),
            ProjectionCalibrationMethod.PiecewiseAffineAt20 =>
                FitAffine(train, method, threshold, piecewise: true),
            _ => new FittedCalibration(ProjectionCalibrationMethod.Identity, 0, 1, 0, 1, threshold)
        };
    }

    public static double Apply(FittedCalibration fit, double v1)
    {
        var y = fit.Method switch
        {
            ProjectionCalibrationMethod.Identity => v1,
            ProjectionCalibrationMethod.GlobalScale => fit.HighSlope * v1,
            ProjectionCalibrationMethod.GlobalAffine => fit.HighIntercept + (fit.HighSlope * v1),
            ProjectionCalibrationMethod.PiecewiseScaleAt20 =>
                v1 < fit.Threshold ? fit.LowSlope * v1 : fit.HighSlope * v1,
            ProjectionCalibrationMethod.PiecewiseAffineAt20 =>
                v1 < fit.Threshold
                    ? fit.LowIntercept + (fit.LowSlope * v1)
                    : fit.HighIntercept + (fit.HighSlope * v1),
            _ => v1
        };
        return Math.Round(Math.Max(0, y), 1, MidpointRounding.AwayFromZero);
    }

    private static FittedCalibration FitScale(
        IReadOnlyList<CalibrationObservation> train,
        ProjectionCalibrationMethod method,
        double threshold)
    {
        var xs = train.Select(o => o.V1Predicted).ToList();
        var ys = train.Select(o => o.Actual).ToList();
        var k = FitSlopeThroughOrigin(xs, ys);
        return new FittedCalibration(method, 0, k, 0, k, threshold);
    }

    private static FittedCalibration FitPiecewiseScale(
        IReadOnlyList<CalibrationObservation> train,
        double threshold)
    {
        var low = train.Where(o => o.V1Predicted < threshold).ToList();
        var high = train.Where(o => o.V1Predicted >= threshold).ToList();
        var kLow = low.Count >= 10
            ? FitSlopeThroughOrigin(low.Select(o => o.V1Predicted).ToList(), low.Select(o => o.Actual).ToList())
            : 1.0;
        var kHigh = high.Count >= 10
            ? FitSlopeThroughOrigin(high.Select(o => o.V1Predicted).ToList(), high.Select(o => o.Actual).ToList())
            : FitSlopeThroughOrigin(train.Select(o => o.V1Predicted).ToList(), train.Select(o => o.Actual).ToList());
        return new FittedCalibration(
            ProjectionCalibrationMethod.PiecewiseScaleAt20,
            0,
            kLow,
            0,
            kHigh,
            threshold);
    }

    private static FittedCalibration FitAffine(
        IReadOnlyList<CalibrationObservation> train,
        ProjectionCalibrationMethod method,
        double threshold,
        bool piecewise)
    {
        if (!piecewise)
        {
            var (a, b) = FitOrdinaryLeastSquares(
                train.Select(o => o.V1Predicted).ToList(),
                train.Select(o => o.Actual).ToList());
            return new FittedCalibration(method, a, b, a, b, threshold);
        }

        var low = train.Where(o => o.V1Predicted < threshold).ToList();
        var high = train.Where(o => o.V1Predicted >= threshold).ToList();

        // Below threshold bias was near zero — keep identity unless sample strongly disagrees.
        double lowA = 0;
        double lowB = 1;
        if (low.Count >= 20)
        {
            var (a, b) = FitOrdinaryLeastSquares(
                low.Select(o => o.V1Predicted).ToList(),
                low.Select(o => o.Actual).ToList());
            // Only adopt if it does not inflate MAE on the low slice vs identity.
            var idMae = MaePairs(low.Select(o => (o.V1Predicted, o.Actual)));
            var affMae = MaePairs(low.Select(o => (Math.Max(0, a + b * o.V1Predicted), o.Actual)));
            if (affMae + 0.05 < idMae)
            {
                lowA = a;
                lowB = b;
            }
        }

        var (highA, highB) = high.Count >= 20
            ? FitOrdinaryLeastSquares(
                high.Select(o => o.V1Predicted).ToList(),
                high.Select(o => o.Actual).ToList())
            : FitOrdinaryLeastSquares(
                train.Select(o => o.V1Predicted).ToList(),
                train.Select(o => o.Actual).ToList());

        return new FittedCalibration(method, lowA, lowB, highA, highB, threshold);
    }

    private static double FitSlopeThroughOrigin(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        double num = 0;
        double den = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            num += xs[i] * ys[i];
            den += xs[i] * xs[i];
        }

        if (den < 1e-9)
        {
            return 1;
        }

        return Math.Clamp(num / den, 0.20, 1.50);
    }

    private static (double Intercept, double Slope) FitOrdinaryLeastSquares(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys)
    {
        var n = xs.Count;
        if (n < 2)
        {
            return (0, 1);
        }

        var meanX = xs.Average();
        var meanY = ys.Average();
        double num = 0;
        double den = 0;
        for (var i = 0; i < n; i++)
        {
            num += (xs[i] - meanX) * (ys[i] - meanY);
            den += (xs[i] - meanX) * (xs[i] - meanX);
        }

        if (den < 1e-9)
        {
            return (meanY, 0);
        }

        var slope = Math.Clamp(num / den, 0.20, 1.50);
        var intercept = meanY - (slope * meanX);
        // Keep intercept from exploding — clamp to a modest fantasy-point offset.
        intercept = Math.Clamp(intercept, -5.0, 8.0);
        return (intercept, slope);
    }

    private static double Mae(IReadOnlyList<CalibrationObservation> rows, Func<CalibrationObservation, double> pred) =>
        MaePairs(rows.Select(o => (pred(o), o.Actual)));

    private static double Bias(IReadOnlyList<CalibrationObservation> rows, Func<CalibrationObservation, double> pred) =>
        BiasPairs(rows.Select(o => (pred(o), o.Actual)));

    private static double MaePairs(IEnumerable<(double Pred, double Actual)> rows)
    {
        var list = rows.ToList();
        return list.Count == 0 ? 0 : list.Average(r => Math.Abs(r.Actual - r.Pred));
    }

    private static double BiasPairs(IEnumerable<(double Pred, double Actual)> rows)
    {
        var list = rows.ToList();
        return list.Count == 0 ? 0 : list.Average(r => r.Actual - r.Pred);
    }
}
