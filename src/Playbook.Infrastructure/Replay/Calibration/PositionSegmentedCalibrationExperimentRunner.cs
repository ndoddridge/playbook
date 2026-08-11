using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playbook.Application.Replay;
using Playbook.Core.Players;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay.Reconstruction;

namespace Playbook.Infrastructure.Replay.Calibration;

/// <summary>
/// Position-Segmented Projection Calibration V1 orchestration: decide a defensible position
/// grouping from real development-season counts, fit per-group piecewise calibration via LOOCV
/// (reusing <see cref="ProjectionCalibrationFitter"/> — no new fitting math), gate on development
/// results, and only touch the 2024 holdout once if the development gate passes.
/// </summary>
public sealed class PositionSegmentedCalibrationExperimentRunner
{
    private static readonly IReadOnlyList<Position> CandidatePositions =
        [Position.QB, Position.RB, Position.WR, Position.TE];

    private readonly IServiceProvider _services;
    private readonly ILogger<PositionSegmentedCalibrationExperimentRunner> _logger;

    public PositionSegmentedCalibrationExperimentRunner(
        IServiceProvider services,
        ILogger<PositionSegmentedCalibrationExperimentRunner> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task<PositionSegmentedCalibrationExperimentReport> RunOfficialExperimentAsync(
        CancellationToken cancellationToken = default)
    {
        var developmentSeasons = PositionSegmentedCalibrationExperiment.DevelopmentSeasons.ToList();
        var holdout = PositionSegmentedCalibrationExperiment.HoldoutSeason;

        var state = _services.GetRequiredService<HistoricalProjectionExperimentState>();
        var segmentedState = _services.GetRequiredService<PositionSegmentedCalibrationState>();
        var seasonRunner = _services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
        var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();

        // 1) Collect development observations under V1 (never touch holdout).
        state.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV1;
        var developmentCards = new List<SeasonScorecard>();
        foreach (var season in developmentSeasons)
        {
            var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "PositionSegmentedCalibrationV1: collecting V1 development season {Season}", season);
            developmentCards.Add(await seasonRunner.RunAsync(
                    new MultiWeekReplayRequest
                    {
                        Season = season,
                        StartWeek = 1,
                        EndWeek = end,
                        FixtureId = "nflverse",
                        ContinueOnWeekFailure = true
                    },
                    cancellationToken)
                .ConfigureAwait(false));
        }

        var observations = developmentCards
            .SelectMany(c => c.ProjectionEvaluations)
            .Where(p => p.BaselineOpportunityAwarePoints is not null && p.BaselineRecentAveragePoints is not null)
            .Select(p => new CalibrationObservation
            {
                Season = p.Season,
                Week = p.Week,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                Position = p.Position,
                V1Predicted = p.BaselineOpportunityAwarePoints!.Value,
                Actual = p.ActualPoints,
                BaselineAPredicted = p.BaselineRecentAveragePoints!.Value
            })
            .ToList();

        if (observations.Any(o => o.Season == holdout))
        {
            throw new InvalidOperationException("Holdout leaked into development observation set.");
        }

        // 2) Decide grouping from real per-season counts (not assumed up front).
        var (groupDefs, groupingRationale) = DecideGrouping(observations, developmentSeasons);
        _logger.LogInformation("PositionSegmentedCalibrationV1: grouping — {Rationale}", groupingRationale);

        // 3) Per-group LOOCV fit on development only, reusing ProjectionCalibrationFitter.
        var groupSummaries = new List<PositionGroupSummary>();
        foreach (var (label, positions) in groupDefs)
        {
            var groupObs = observations.Where(o => positions.Contains(o.Position)).ToList();
            var selection = ProjectionCalibrationFitter.SelectAndFreeze(groupObs, developmentSeasons, holdout);

            var folds = new List<PositionGroupFoldResult>();
            foreach (var fold in selection.Folds)
            {
                var val = groupObs.Where(o => o.Season == fold.ValidateSeason).ToList();
                var valMaeGlobal = val.Count == 0
                    ? 0
                    : val.Average(o => Math.Abs(o.Actual - FrozenProjectionCalibrationV2.Apply(o.V1Predicted)));
                folds.Add(new PositionGroupFoldResult
                {
                    GroupLabel = label,
                    ValidateSeason = fold.ValidateSeason,
                    TrainingObservationCount = groupObs.Count - val.Count,
                    ValidationObservationCount = val.Count,
                    Method = fold.Calibration.Method,
                    LowIntercept = fold.Calibration.LowIntercept,
                    LowSlope = fold.Calibration.LowSlope,
                    HighIntercept = fold.Calibration.HighIntercept,
                    HighSlope = fold.Calibration.HighSlope,
                    ValMaeGlobalV2 = Math.Round(valMaeGlobal, 2),
                    ValMaeSegmented = Math.Round(fold.MaeV2, 2)
                });
            }

            var pooledMaeGlobal = groupObs.Count == 0
                ? 0
                : groupObs.Average(o => Math.Abs(o.Actual - FrozenProjectionCalibrationV2.Apply(o.V1Predicted)));
            var pooledBiasGlobal = groupObs.Count == 0
                ? 0
                : groupObs.Average(o => o.Actual - FrozenProjectionCalibrationV2.Apply(o.V1Predicted));

            groupSummaries.Add(new PositionGroupSummary
            {
                GroupLabel = label,
                Positions = positions,
                ObservationsPerSeason = developmentSeasons.ToDictionary(
                    s => s, s => groupObs.Count(o => o.Season == s)),
                TotalObservations = groupObs.Count,
                Folds = folds,
                PooledLoocvMaeGlobalV2 = Math.Round(pooledMaeGlobal, 2),
                PooledLoocvMaeSegmented = Math.Round(selection.PooledDevMaeV2, 2),
                PooledLoocvBiasGlobalV2 = Math.Round(pooledBiasGlobal, 2),
                PooledLoocvBiasSegmented = Math.Round(selection.PooledDevBiasV2, 2),
                FrozenMethod = selection.Frozen.Method,
                FrozenLowIntercept = selection.Frozen.LowIntercept,
                FrozenLowSlope = selection.Frozen.LowSlope,
                FrozenHighIntercept = selection.Frozen.HighIntercept,
                FrozenHighSlope = selection.Frozen.HighSlope
            });
        }

        // 4) Pooled (all groups) development metrics — weighted by group N (equal to full pooling
        // since each observation belongs to exactly one group).
        var totalN = groupSummaries.Sum(g => g.TotalObservations);
        double Weighted(Func<PositionGroupSummary, double> select) =>
            totalN == 0 ? 0 : groupSummaries.Sum(g => select(g) * g.TotalObservations) / totalN;

        var devPooledMaeGlobalV2 = Math.Round(Weighted(g => g.PooledLoocvMaeGlobalV2), 2);
        var devPooledMaeSegmented = Math.Round(Weighted(g => g.PooledLoocvMaeSegmented), 2);
        var devPooledBiasGlobalV2 = Math.Round(Weighted(g => g.PooledLoocvBiasGlobalV2), 2);
        var devPooledBiasSegmented = Math.Round(Weighted(g => g.PooledLoocvBiasSegmented), 2);

        // 5) Freeze per-group calibrations (refit on ALL development observations — computed
        // above, before any holdout data is touched) and build the position -> fit map used by
        // PositionSegmentedCalibratedProjectionEngine.
        var frozenByPosition = BuildFrozenMap(groupSummaries);

        // 6) Development decision impact: global V2 (control) vs segmented (candidate).
        var devImpact = await MeasureDecisionImpactAsync(
                developmentSeasons,
                scopeSeasonLabel: 0,
                frozenByPosition,
                cancellationToken)
            .ConfigureAwait(false);

        // 7) Pre-registered development gate — decided before any holdout data is touched.
        var (devJustifiesHoldout, devGateRationale) = EvaluateDevGate(
            devPooledMaeGlobalV2, devPooledMaeSegmented, groupSummaries, devImpact);
        _logger.LogInformation(
            "PositionSegmentedCalibrationV1: dev gate justifiesHoldout={Justifies} — {Rationale}",
            devJustifiesHoldout, devGateRationale);

        if (!devJustifiesHoldout)
        {
            state.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV1;
            segmentedState.Active = null;
            return BuildReport(
                groupingRationale, groupSummaries, devPooledMaeGlobalV2, devPooledMaeSegmented,
                devPooledBiasGlobalV2, devPooledBiasSegmented, devImpact,
                devJustifiesHoldout: false, devGateRationale,
                holdoutMaeGlobalV2: null, holdoutMaeSegmented: null,
                holdoutBiasGlobalV2: null, holdoutBiasSegmented: null,
                holdoutByPosition: null, holdoutImpact: null,
                verdict: ProjectionExperimentVerdict.NoMaterialImprovement,
                verdictRationale: "Development results did not clear the pre-registered gate; " +
                                  "2024 holdout was not run. " + devGateRationale);
        }

        // 8) ONE official holdout evaluation: global V2 primary (control) and position-segmented
        // primary (candidate). Raw point predictions (V1 / global V2) are populated on every
        // evaluation regardless of primary mode, so the global-primary pass also supplies the
        // point-metric comparison — no separate third replay pass is needed.
        _logger.LogInformation(
            "PositionSegmentedCalibrationV1: official holdout {Season} — global V2 then segmented (single pass each)",
            holdout);

        state.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV2;
        segmentedState.Active = null;
        var holdoutEnd = await calendar.GetRegularSeasonEndWeekAsync(holdout, cancellationToken).ConfigureAwait(false);
        var holdoutGlobalCard = await seasonRunner.RunAsync(
                new MultiWeekReplayRequest
                {
                    Season = holdout,
                    StartWeek = 1,
                    EndWeek = holdoutEnd,
                    FixtureId = "nflverse",
                    ContinueOnWeekFailure = true
                },
                cancellationToken)
            .ConfigureAwait(false);

        state.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV2PositionSegmented;
        segmentedState.Active = frozenByPosition;
        var holdoutSegmentedCard = await seasonRunner.RunAsync(
                new MultiWeekReplayRequest
                {
                    Season = holdout,
                    StartWeek = 1,
                    EndWeek = holdoutEnd,
                    FixtureId = "nflverse",
                    ContinueOnWeekFailure = true
                },
                cancellationToken)
            .ConfigureAwait(false);

        state.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV1;
        segmentedState.Active = null;

        var inScopePositions = groupDefs.SelectMany(g => g.Positions).ToHashSet();
        var holdoutFair = holdoutGlobalCard.ProjectionEvaluations
            .Where(p =>
                p.BaselineOpportunityAwarePoints is not null &&
                p.ProjectionV2Points is not null &&
                inScopePositions.Contains(p.Position))
            .ToList();

        double SegmentedApply(Position position, double v1) =>
            frozenByPosition.TryGetValue(position, out var fit)
                ? ProjectionCalibrationFitter.Apply(fit, v1)
                : FrozenProjectionCalibrationV2.Apply(v1);

        var holdoutMaeGlobalV2 = holdoutFair.Count == 0
            ? (double?)null
            : Math.Round(holdoutFair.Average(p => Math.Abs(p.ActualPoints - p.ProjectionV2Points!.Value)), 2);
        var holdoutMaeSegmented = holdoutFair.Count == 0
            ? (double?)null
            : Math.Round(
                holdoutFair.Average(p => Math.Abs(
                    p.ActualPoints - SegmentedApply(p.Position, p.BaselineOpportunityAwarePoints!.Value))),
                2);
        var holdoutBiasGlobalV2 = holdoutFair.Count == 0
            ? (double?)null
            : Math.Round(holdoutFair.Average(p => p.ActualPoints - p.ProjectionV2Points!.Value), 2);
        var holdoutBiasSegmented = holdoutFair.Count == 0
            ? (double?)null
            : Math.Round(
                holdoutFair.Average(p =>
                    p.ActualPoints - SegmentedApply(p.Position, p.BaselineOpportunityAwarePoints!.Value)),
                2);

        var holdoutByPosition = groupDefs.Select(g =>
        {
            var rows = holdoutFair.Where(p => g.Positions.Contains(p.Position)).ToList();
            return new PositionSliceMetrics
            {
                GroupLabel = g.Label,
                N = rows.Count,
                MaeGlobalV2 = rows.Count == 0
                    ? null
                    : Math.Round(rows.Average(p => Math.Abs(p.ActualPoints - p.ProjectionV2Points!.Value)), 2),
                MaeSegmented = rows.Count == 0
                    ? null
                    : Math.Round(
                        rows.Average(p => Math.Abs(
                            p.ActualPoints - SegmentedApply(p.Position, p.BaselineOpportunityAwarePoints!.Value))),
                        2),
                BiasGlobalV2 = rows.Count == 0
                    ? null
                    : Math.Round(rows.Average(p => p.ActualPoints - p.ProjectionV2Points!.Value), 2),
                BiasSegmented = rows.Count == 0
                    ? null
                    : Math.Round(
                        rows.Average(p =>
                            p.ActualPoints - SegmentedApply(p.Position, p.BaselineOpportunityAwarePoints!.Value)),
                        2)
            };
        }).ToList();

        var holdoutImpact = BuildDecisionImpact(
            holdout,
            holdoutGlobalCard.AllGrades,
            holdoutSegmentedCard.AllGrades);

        var (verdict, verdictRationale) = JudgeHoldout(
            devGateRationale, holdoutMaeGlobalV2, holdoutMaeSegmented,
            holdoutBiasGlobalV2, holdoutBiasSegmented, holdoutImpact);

        return BuildReport(
            groupingRationale, groupSummaries, devPooledMaeGlobalV2, devPooledMaeSegmented,
            devPooledBiasGlobalV2, devPooledBiasSegmented, devImpact,
            devJustifiesHoldout: true, devGateRationale,
            holdoutMaeGlobalV2, holdoutMaeSegmented, holdoutBiasGlobalV2, holdoutBiasSegmented,
            holdoutByPosition, holdoutImpact, verdict, verdictRationale);
    }

    /// <summary>
    /// Candidate positions QB/RB/WR/TE only — K/DST have a different scoring shape and are not
    /// part of this experiment; <see cref="PositionSegmentedCalibratedProjectionEngine"/> already
    /// falls back to the global frozen calibration for any position with no configured group.
    /// A position only gets its own group if it clears
    /// <see cref="PositionSegmentedCalibrationExperiment.MinObservationsPerSeasonForOwnGroup"/> in
    /// EVERY development season; otherwise TE folds into WR (standard "pass-catcher" grouping).
    /// Decided here from real counts, not assumed up front.
    /// </summary>
    public static (List<(string Label, List<Position> Positions)> Groups, string Rationale) DecideGrouping(
        IReadOnlyList<CalibrationObservation> observations,
        IReadOnlyList<int> developmentSeasons)
    {
        var perSeasonCounts = CandidatePositions.ToDictionary(
            p => p,
            p => developmentSeasons.ToDictionary(s => s, s => observations.Count(o => o.Position == p && o.Season == s)));

        bool ClearsThreshold(Position p) =>
            developmentSeasons.All(s =>
                perSeasonCounts[p][s] >= PositionSegmentedCalibrationExperiment.MinObservationsPerSeasonForOwnGroup);

        var groups = new List<(string Label, List<Position> Positions)>
        {
            ("QB", [Position.QB]),
            ("RB", [Position.RB])
        };

        var teFolded = !ClearsThreshold(Position.TE);
        if (teFolded)
        {
            groups.Add(("WR/TE", [Position.WR, Position.TE]));
        }
        else
        {
            groups.Add(("WR", [Position.WR]));
            groups.Add(("TE", [Position.TE]));
        }

        foreach (var (label, positions) in groups)
        {
            if (positions.Count == 1 && !ClearsThreshold(positions[0]))
            {
                throw new InvalidOperationException(
                    $"{label}: insufficient per-season development observations (need >= " +
                    $"{PositionSegmentedCalibrationExperiment.MinObservationsPerSeasonForOwnGroup} in every of " +
                    $"{string.Join(',', developmentSeasons)}) and no adjacent group in this candidate set " +
                    "(QB/RB/WR/TE) is football-defensible to fold it into.");
            }
        }

        var countsText = string.Join("; ", CandidatePositions.Select(p =>
            $"{p}: " + string.Join(',', developmentSeasons.Select(s => $"{s}={perSeasonCounts[p][s]}"))));

        var rationale =
            "Candidate positions QB/RB/WR/TE; K/DST excluded (different scoring shape, not part of this " +
            "experiment — falls back to global Projection V2). Threshold: >= " +
            $"{PositionSegmentedCalibrationExperiment.MinObservationsPerSeasonForOwnGroup} observations in " +
            $"EVERY development season ({string.Join(',', developmentSeasons)}) required for a position to " +
            $"justify its own group. Per-season counts — {countsText}. " +
            (teFolded
                ? "TE fell short of the threshold in at least one development season and was folded into WR " +
                  "(standard \"pass-catcher\" grouping) rather than assumed up front."
                : "QB, RB, WR, and TE each cleared the threshold in every development season and were kept as " +
                  "four separate groups.");

        return (groups, rationale);
    }

    private static Dictionary<Position, ProjectionCalibrationFitter.FittedCalibration> BuildFrozenMap(
        IReadOnlyList<PositionGroupSummary> groups)
    {
        var map = new Dictionary<Position, ProjectionCalibrationFitter.FittedCalibration>();
        foreach (var g in groups)
        {
            var fit = new ProjectionCalibrationFitter.FittedCalibration(
                g.FrozenMethod, g.FrozenLowIntercept, g.FrozenLowSlope, g.FrozenHighIntercept, g.FrozenHighSlope, 20);
            foreach (var position in g.Positions)
            {
                map[position] = fit;
            }
        }

        return map;
    }

    private static (bool Justifies, string Rationale) EvaluateDevGate(
        double devPooledMaeGlobalV2,
        double devPooledMaeSegmented,
        IReadOnlyList<PositionGroupSummary> groups,
        DecisionImpactReport devImpact)
    {
        var relImp = devPooledMaeGlobalV2 <= 1e-9
            ? 0
            : (devPooledMaeGlobalV2 - devPooledMaeSegmented) / devPooledMaeGlobalV2;
        var maeOk = relImp >= PositionSegmentedCalibrationSuccessCriteria.MinRelativeMaeImprovementDev;

        var worstGroup = groups
            .Select(g => new
            {
                g.GroupLabel,
                RelRegression = g.PooledLoocvMaeGlobalV2 <= 1e-9
                    ? 0
                    : (g.PooledLoocvMaeSegmented - g.PooledLoocvMaeGlobalV2) / g.PooledLoocvMaeGlobalV2
            })
            .OrderByDescending(x => x.RelRegression)
            .First();
        var noGroupCatastrophic =
            worstGroup.RelRegression <= PositionSegmentedCalibrationSuccessCriteria.MaxRelativeMaeRegressionAnyGroup;

        var decisionDelta = (devImpact.TotalDecisionValueV2 ?? 0) - (devImpact.TotalDecisionValueV1 ?? 0);
        var decisionOk = decisionDelta >= -PositionSegmentedCalibrationSuccessCriteria.MaxDecisionValueDegradationDev;

        var justifies = maeOk && noGroupCatastrophic && decisionOk;
        var rationale =
            $"Pooled dev LOOCV MAE improvement={relImp:P1} (need >=" +
            $"{PositionSegmentedCalibrationSuccessCriteria.MinRelativeMaeImprovementDev:P0}, ok={maeOk}); " +
            $"worst per-group regression=[{worstGroup.GroupLabel}]{worstGroup.RelRegression:P1} (cap " +
            $"{PositionSegmentedCalibrationSuccessCriteria.MaxRelativeMaeRegressionAnyGroup:P0}, ok=" +
            $"{noGroupCatastrophic}); dev decision value Δ={decisionDelta:0.0} (floor -" +
            $"{PositionSegmentedCalibrationSuccessCriteria.MaxDecisionValueDegradationDev:0}, ok={decisionOk}).";

        return (justifies, rationale);
    }

    private static (ProjectionExperimentVerdict Verdict, string Rationale) JudgeHoldout(
        string devGateRationale,
        double? holdoutMaeGlobalV2,
        double? holdoutMaeSegmented,
        double? holdoutBiasGlobalV2,
        double? holdoutBiasSegmented,
        DecisionImpactReport holdoutImpact)
    {
        if (holdoutMaeGlobalV2 is null || holdoutMaeSegmented is null ||
            holdoutBiasGlobalV2 is null || holdoutBiasSegmented is null)
        {
            return (ProjectionExperimentVerdict.Inconclusive, "Missing holdout metrics.");
        }

        var maeImp = holdoutMaeGlobalV2.Value <= 1e-9
            ? 0
            : (holdoutMaeGlobalV2.Value - holdoutMaeSegmented.Value) / holdoutMaeGlobalV2.Value;
        var biasImp = Math.Abs(holdoutBiasGlobalV2.Value) <= 1e-9
            ? 0
            : (Math.Abs(holdoutBiasGlobalV2.Value) - Math.Abs(holdoutBiasSegmented.Value)) /
              Math.Abs(holdoutBiasGlobalV2.Value);
        var decisionDelta = (holdoutImpact.TotalDecisionValueV2 ?? 0) - (holdoutImpact.TotalDecisionValueV1 ?? 0);

        // Pre-registered holdout bar (decided here, before any holdout data was touched): the
        // segmented model must not make MAE or |bias| worse than the already-accepted global V2
        // control, and Start/Sit decision value must not degrade beyond the same tolerance used
        // for the development gate.
        var decisionOk = decisionDelta >= -PositionSegmentedCalibrationSuccessCriteria.MaxDecisionValueDegradationDev;

        if (maeImp < 0 || biasImp < 0)
        {
            return (
                ProjectionExperimentVerdict.Regression,
                $"Holdout worsened vs global V2 control (MAE Δ={maeImp:P1}, |bias| Δ={biasImp:P1}). " +
                "Keep global Projection V2 (no position segmentation).");
        }

        if (!decisionOk)
        {
            return (
                ProjectionExperimentVerdict.Inconclusive,
                $"Holdout projection metrics did not worsen (MAE Δ={maeImp:P1}, |bias| Δ={biasImp:P1}) but " +
                $"Start/Sit decision value degraded by {-decisionDelta:0.0} (> " +
                $"{PositionSegmentedCalibrationSuccessCriteria.MaxDecisionValueDegradationDev:0}). Do not auto-accept.");
        }

        if (maeImp > 0 || biasImp > 0)
        {
            return (
                ProjectionExperimentVerdict.Improvement,
                $"Dev gate: {devGateRationale} Holdout MAE Δ={maeImp:P1}, |bias| Δ={biasImp:P1}, decision " +
                $"value Δ={decisionDelta:0.0} vs global V2 control. Position-segmented calibration beats the " +
                "existing frozen control on the 2024 holdout.");
        }

        return (
            ProjectionExperimentVerdict.Inconclusive,
            $"Holdout metrics tied global V2 control (MAE Δ={maeImp:P1}, |bias| Δ={biasImp:P1}). " +
            "No material difference on the holdout.");
    }

    private async Task<DecisionImpactReport> MeasureDecisionImpactAsync(
        IReadOnlyList<int> seasons,
        int scopeSeasonLabel,
        IReadOnlyDictionary<Position, ProjectionCalibrationFitter.FittedCalibration> frozenByPosition,
        CancellationToken cancellationToken)
    {
        var state = _services.GetRequiredService<HistoricalProjectionExperimentState>();
        var segmentedState = _services.GetRequiredService<PositionSegmentedCalibrationState>();
        var seasonRunner = _services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
        var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();

        async Task<List<SeasonScorecard>> RunAll(HistoricalProjectionPrimaryMode mode)
        {
            state.PrimaryMode = mode;
            segmentedState.Active = mode == HistoricalProjectionPrimaryMode.ProjectionV2PositionSegmented
                ? frozenByPosition
                : null;
            var cards = new List<SeasonScorecard>();
            foreach (var season in seasons)
            {
                var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken).ConfigureAwait(false);
                cards.Add(await seasonRunner.RunAsync(
                        new MultiWeekReplayRequest
                        {
                            Season = season,
                            StartWeek = 1,
                            EndWeek = end,
                            FixtureId = "nflverse",
                            ContinueOnWeekFailure = true
                        },
                        cancellationToken)
                    .ConfigureAwait(false));
            }

            return cards;
        }

        var globalCards = await RunAll(HistoricalProjectionPrimaryMode.ProjectionV2).ConfigureAwait(false);
        var segmentedCards = await RunAll(HistoricalProjectionPrimaryMode.ProjectionV2PositionSegmented)
            .ConfigureAwait(false);
        state.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV1;
        segmentedState.Active = null;

        return BuildDecisionImpact(
            scopeSeasonLabel,
            globalCards.SelectMany(c => c.AllGrades).ToList(),
            segmentedCards.SelectMany(c => c.AllGrades).ToList());
    }

    /// <summary>V1-field-slot = global V2 control; V2-field-slot = position-segmented candidate.</summary>
    private static DecisionImpactReport BuildDecisionImpact(
        int scopeSeasonLabel,
        IReadOnlyList<ReplayDecisionGrade> controlGrades,
        IReadOnlyList<ReplayDecisionGrade> candidateGrades)
    {
        var byPlayerWeekControl = controlGrades
            .GroupBy(g => (g.Season, g.Week, g.PlayerId)).ToDictionary(x => x.Key, x => x.First());
        var byPlayerWeekCandidate = candidateGrades
            .GroupBy(g => (g.Season, g.Week, g.PlayerId)).ToDictionary(x => x.Key, x => x.First());

        var changed = 0;
        var improved = 0;
        var worsened = 0;
        var unchangedOutcome = 0;
        foreach (var key in byPlayerWeekControl.Keys.Intersect(byPlayerWeekCandidate.Keys))
        {
            var a = byPlayerWeekControl[key];
            var b = byPlayerWeekCandidate[key];
            var recChanged = a.Recommendation != b.Recommendation ||
                             Math.Abs(a.ExpectedValue - b.ExpectedValue) >= 0.05;
            if (!recChanged)
            {
                continue;
            }

            changed++;
            if (a.WasCorrect is null || b.WasCorrect is null ||
                a.ActualDecisionDifferential is null || b.ActualDecisionDifferential is null)
            {
                unchangedOutcome++;
                continue;
            }

            if (b.ActualDecisionDifferential.Value > a.ActualDecisionDifferential.Value + 0.05)
            {
                improved++;
            }
            else if (b.ActualDecisionDifferential.Value + 0.05 < a.ActualDecisionDifferential.Value)
            {
                worsened++;
            }
            else
            {
                unchangedOutcome++;
            }
        }

        return new DecisionImpactReport
        {
            ScopeSeason = scopeSeasonLabel,
            TotalDecisionsV1 = controlGrades.Count,
            TotalDecisionsV2 = candidateGrades.Count,
            AccuracyV1 = Accuracy(controlGrades),
            AccuracyV2 = Accuracy(candidateGrades),
            AverageDecisionValueV1 = AvgDiff(controlGrades),
            AverageDecisionValueV2 = AvgDiff(candidateGrades),
            TotalDecisionValueV1 = SumDiff(controlGrades),
            TotalDecisionValueV2 = SumDiff(candidateGrades),
            DecisionsChanged = changed,
            ChangedImproved = improved,
            ChangedWorsened = worsened,
            ChangedUnchangedOutcome = unchangedOutcome,
            GradedDecisionsV1 = controlGrades.Count(g => g.WasCorrect is not null),
            GradedDecisionsV2 = candidateGrades.Count(g => g.WasCorrect is not null)
        };
    }

    private static double? Accuracy(IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var graded = grades.Where(g => g.WasCorrect is not null).ToList();
        return graded.Count == 0 ? null : Math.Round(100.0 * graded.Count(g => g.WasCorrect == true) / graded.Count, 1);
    }

    private static double? AvgDiff(IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var diffs = grades.Where(g => g.ActualDecisionDifferential is not null)
            .Select(g => g.ActualDecisionDifferential!.Value).ToList();
        return diffs.Count == 0 ? null : Math.Round(diffs.Average(), 2);
    }

    private static double? SumDiff(IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var diffs = grades.Where(g => g.ActualDecisionDifferential is not null)
            .Select(g => g.ActualDecisionDifferential!.Value).ToList();
        return diffs.Count == 0 ? null : Math.Round(diffs.Sum(), 2);
    }

    private static PositionSegmentedCalibrationExperimentReport BuildReport(
        string groupingRationale,
        IReadOnlyList<PositionGroupSummary> groups,
        double devPooledMaeGlobalV2,
        double devPooledMaeSegmented,
        double devPooledBiasGlobalV2,
        double devPooledBiasSegmented,
        DecisionImpactReport devImpact,
        bool devJustifiesHoldout,
        string devGateRationale,
        double? holdoutMaeGlobalV2,
        double? holdoutMaeSegmented,
        double? holdoutBiasGlobalV2,
        double? holdoutBiasSegmented,
        IReadOnlyList<PositionSliceMetrics>? holdoutByPosition,
        DecisionImpactReport? holdoutImpact,
        ProjectionExperimentVerdict verdict,
        string verdictRationale) =>
        new()
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Hypothesis =
                "Fitting separate piecewise calibration parameters per position group reduces projection " +
                "error and/or improves Start/Sit decision value beyond the single global frozen Projection V2 " +
                "curve, because different positions have different volume/scoring distributions that one " +
                "global piecewise curve cannot capture.",
            SuccessCriteriaText = PositionSegmentedCalibrationSuccessCriteria.Text,
            DevelopmentSeasons = PositionSegmentedCalibrationExperiment.DevelopmentSeasons,
            HoldoutSeason = PositionSegmentedCalibrationExperiment.HoldoutSeason,
            GroupingRationale = groupingRationale,
            Groups = groups,
            DevPooledMaeGlobalV2 = devPooledMaeGlobalV2,
            DevPooledMaeSegmented = devPooledMaeSegmented,
            DevPooledBiasGlobalV2 = devPooledBiasGlobalV2,
            DevPooledBiasSegmented = devPooledBiasSegmented,
            DevelopmentDecisionImpact = devImpact,
            DevJustifiesHoldout = devJustifiesHoldout,
            DevGateRationale = devGateRationale,
            HoldoutMaeGlobalV2 = holdoutMaeGlobalV2,
            HoldoutMaeSegmented = holdoutMaeSegmented,
            HoldoutBiasGlobalV2 = holdoutBiasGlobalV2,
            HoldoutBiasSegmented = holdoutBiasSegmented,
            HoldoutByPosition = holdoutByPosition,
            HoldoutDecisionImpact = holdoutImpact,
            Verdict = verdict,
            VerdictRationale = verdictRationale,
            UsedHoldoutDuringFitting = false
        };
}
