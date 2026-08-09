using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playbook.Application.Replay;
using Playbook.Core.Knowledge;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;

namespace Playbook.Infrastructure.Knowledge;

/// <summary>
/// Knowledge Impact Experiment V1: ablation of knowledge groups under frozen
/// Projection V2 + Confidence V2 + Decision Policy Off.
/// Fits on development seasons only; ONE official 2024 holdout after freeze.
/// </summary>
public sealed class KnowledgeImpactExperimentRunner
{
    private readonly IServiceProvider _services;
    private readonly ILogger<KnowledgeImpactExperimentRunner> _logger;

    public KnowledgeImpactExperimentRunner(
        IServiceProvider services,
        ILogger<KnowledgeImpactExperimentRunner> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>Development-only selection (no holdout). Used to freeze constants.</summary>
    public async Task<(KnowledgeImpactGroup Selected, IReadOnlyList<string> Summaries, IReadOnlyDictionary<KnowledgeImpactGroup, double> MeanValDelta)>
        RunDevelopmentSelectionAsync(CancellationToken cancellationToken = default)
    {
        var developmentSeasons = FrozenKnowledgeImpactExperimentV1.DevelopmentSeasons.ToList();
        var holdout = FrozenKnowledgeImpactExperimentV1.HoldoutSeason;

        var projectionState = _services.GetRequiredService<HistoricalProjectionExperimentState>();
        var policyState = _services.GetRequiredService<ConfidenceAwareDecisionPolicyState>();
        var knowledgeState = _services.GetRequiredService<KnowledgeImpactExperimentState>();
        var previousProjection = projectionState.PrimaryMode;
        var previousPolicy = policyState.Mode;
        var previousMode = knowledgeState.Mode;
        var previousGroups = knowledgeState.ActiveGroups;

        projectionState.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV2;
        policyState.Mode = ConfidenceAwareDecisionPolicyMode.Off;

        try
        {
            var seasonRunner = _services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
            var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();

            var baseline = await RunSeasonsAsync(
                    seasonRunner, calendar, developmentSeasons,
                    KnowledgeMode.Baseline, KnowledgeImpactGroup.None, knowledgeState, cancellationToken)
                .ConfigureAwait(false);
            if (baseline.Any(c => c.Season == holdout))
            {
                throw new InvalidOperationException("Holdout leaked into development set.");
            }

            var summaries = new List<string>();
            var meanVal = new Dictionary<KnowledgeImpactGroup, double>();
            foreach (var group in SupportedGroups())
            {
                var enhanced = await RunSeasonsAsync(
                        seasonRunner, calendar, developmentSeasons,
                        KnowledgeMode.Enhanced, group, knowledgeState, cancellationToken)
                    .ConfigureAwait(false);
                var foldDeltas = new List<double>();
                foreach (var val in developmentSeasons)
                {
                    var baseTot = TotalDecisionValue(baseline.Where(c => c.Season == val));
                    var enhTot = TotalDecisionValue(enhanced.Where(c => c.Season == val));
                    var delta = enhTot - baseTot;
                    foldDeltas.Add(delta);
                    summaries.Add($"{group} val={val}: base={baseTot:0.00} enh={enhTot:0.00} Δ={delta:0.00}");
                }

                meanVal[group] = foldDeltas.Average();
            }

            var selected = SelectGroups(meanVal);
            summaries.Add(
                $"finalFreeze={selected} " +
                string.Join(" ", meanVal.Select(kv => $"{kv.Key}Δ={kv.Value:0.00}")));
            return (selected, summaries, meanVal);
        }
        finally
        {
            projectionState.PrimaryMode = previousProjection;
            policyState.Mode = previousPolicy;
            knowledgeState.Mode = previousMode;
            knowledgeState.ActiveGroups = previousGroups;
        }
    }

    public async Task<KnowledgeImpactExperimentReport> RunOfficialExperimentAsync(
        CancellationToken cancellationToken = default)
    {
        var developmentSeasons = FrozenKnowledgeImpactExperimentV1.DevelopmentSeasons.ToList();
        var holdout = FrozenKnowledgeImpactExperimentV1.HoldoutSeason;

        var projectionState = _services.GetRequiredService<HistoricalProjectionExperimentState>();
        var policyState = _services.GetRequiredService<ConfidenceAwareDecisionPolicyState>();
        var knowledgeState = _services.GetRequiredService<KnowledgeImpactExperimentState>();

        var previousProjection = projectionState.PrimaryMode;
        var previousPolicy = policyState.Mode;
        var previousKnowledgeMode = knowledgeState.Mode;
        var previousGroups = knowledgeState.ActiveGroups;

        projectionState.PrimaryMode = HistoricalProjectionPrimaryMode.ProjectionV2;
        policyState.Mode = ConfidenceAwareDecisionPolicyMode.Off;

        try
        {
            AssertFrozenLayersUnchanged();

            var seasonRunner = _services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
            var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();

            var baselineDev = await RunSeasonsAsync(
                    seasonRunner, calendar, developmentSeasons,
                    KnowledgeMode.Baseline, KnowledgeImpactGroup.None, knowledgeState, cancellationToken)
                .ConfigureAwait(false);

            if (baselineDev.Any(c => c.Season == holdout))
            {
                throw new InvalidOperationException("Holdout leaked into knowledge-impact development set.");
            }

            var looSummaries = new List<string>();
            var ablationRows = new List<KnowledgeImpactAblationRow>();
            var meanValDeltaByGroup = new Dictionary<KnowledgeImpactGroup, double>();
            var enhancedByGroup = new Dictionary<KnowledgeImpactGroup, List<SeasonScorecard>>();

            foreach (var (group, name, coverage) in GroupSpecs())
            {
                var enhanced = await RunSeasonsAsync(
                        seasonRunner, calendar, developmentSeasons,
                        KnowledgeMode.Enhanced, group, knowledgeState, cancellationToken)
                    .ConfigureAwait(false);
                enhancedByGroup[group] = enhanced;

                var foldDeltas = new List<double>();
                foreach (var valSeason in developmentSeasons)
                {
                    var baseTot = TotalDecisionValue(baselineDev.Where(c => c.Season == valSeason));
                    var enhTot = TotalDecisionValue(enhanced.Where(c => c.Season == valSeason));
                    var delta = enhTot - baseTot;
                    foldDeltas.Add(delta);
                    looSummaries.Add(
                        $"{name} val={valSeason}: baseTot={baseTot:0.00} enhTot={enhTot:0.00} Δ={delta:0.00}");
                }

                var meanDelta = foldDeltas.Average();
                meanValDeltaByGroup[group] = meanDelta;

                var devMetrics = BuildScopeMetrics(
                    $"DEV {name}", KnowledgeMode.Enhanced, group, enhanced, baselineDev);

                var groupVerdict = meanDelta >= KnowledgeImpactSuccessCriteria.MinDevMeanTotalValueImprovement
                    ? ProjectionExperimentVerdict.Improvement
                    : meanDelta <= -KnowledgeImpactSuccessCriteria.MinDevMeanTotalValueImprovement
                        ? ProjectionExperimentVerdict.Regression
                        : Math.Abs(meanDelta) < 5
                            ? ProjectionExperimentVerdict.NoMaterialImprovement
                            : ProjectionExperimentVerdict.Inconclusive;

                ablationRows.Add(new KnowledgeImpactAblationRow
                {
                    GroupName = name,
                    Group = group,
                    CoverageNote = coverage,
                    Development = devMetrics,
                    Holdout = null,
                    Verdict = groupVerdict,
                    VerdictRationale = $"Dev LOOCV mean Δ total decision value = {meanDelta:0.00}."
                });
            }

            var selected = SelectGroups(meanValDeltaByGroup);
            AssertFrozenMatchesSelected(selected);
            looSummaries.Add(
                $"finalFreeze={selected} " +
                string.Join(" ", meanValDeltaByGroup.Select(kv => $"{kv.Key}Δ={kv.Value:0.00}")));

            var enhancedDev = await RunSeasonsAsync(
                    seasonRunner, calendar, developmentSeasons,
                    KnowledgeMode.Enhanced, selected, knowledgeState, cancellationToken)
                .ConfigureAwait(false);

            var developmentBaseline = BuildScopeMetrics(
                "DEV BASELINE", KnowledgeMode.Baseline, KnowledgeImpactGroup.None, baselineDev, baselineDev);
            var developmentEnhanced = BuildScopeMetrics(
                "DEV ENHANCED", KnowledgeMode.Enhanced, selected, enhancedDev, baselineDev);

            _logger.LogInformation("KnowledgeImpact: official holdout {Season} BASELINE", holdout);
            var holdoutBaselineCards = await RunSeasonsAsync(
                    seasonRunner, calendar, [holdout],
                    KnowledgeMode.Baseline, KnowledgeImpactGroup.None, knowledgeState, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("KnowledgeImpact: official holdout {Season} ENHANCED {Groups}", holdout, selected);
            var holdoutEnhancedCards = await RunSeasonsAsync(
                    seasonRunner, calendar, [holdout],
                    KnowledgeMode.Enhanced, selected, knowledgeState, cancellationToken)
                .ConfigureAwait(false);

            var holdoutBaseline = BuildScopeMetrics(
                "HOLDOUT BASELINE", KnowledgeMode.Baseline, KnowledgeImpactGroup.None,
                holdoutBaselineCards, holdoutBaselineCards);
            var holdoutEnhanced = BuildScopeMetrics(
                "HOLDOUT ENHANCED", KnowledgeMode.Enhanced, selected,
                holdoutEnhancedCards, holdoutBaselineCards);

            var holdoutAblation = new List<KnowledgeImpactAblationRow>();
            foreach (var row in ablationRows)
            {
                KnowledgeImpactScopeMetrics? holdMetrics = null;
                if (selected.HasFlag(row.Group) && row.Group != selected)
                {
                    var cards = await RunSeasonsAsync(
                            seasonRunner, calendar, [holdout],
                            KnowledgeMode.Enhanced, row.Group, knowledgeState, cancellationToken)
                        .ConfigureAwait(false);
                    holdMetrics = BuildScopeMetrics(
                        $"HOLDOUT {row.GroupName}", KnowledgeMode.Enhanced, row.Group,
                        cards, holdoutBaselineCards);
                }
                else if (selected == row.Group)
                {
                    holdMetrics = holdoutEnhanced;
                }

                holdoutAblation.Add(new KnowledgeImpactAblationRow
                {
                    GroupName = row.GroupName,
                    Group = row.Group,
                    CoverageNote = row.CoverageNote,
                    Development = row.Development,
                    Holdout = holdMetrics,
                    Verdict = JudgeGroupHoldout(row, holdMetrics, meanValDeltaByGroup[row.Group]),
                    VerdictRationale = HoldoutGroupRationale(row, holdMetrics, meanValDeltaByGroup[row.Group])
                });
            }

            var failureNotes = BuildFailureAnalysis(
                holdoutBaselineCards, holdoutEnhancedCards, holdoutBaseline, holdoutEnhanced);
            var verdict = Judge(
                developmentEnhanced, developmentBaseline, holdoutEnhanced, holdoutBaseline,
                meanValDeltaByGroup, selected);

            return new KnowledgeImpactExperimentReport
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Hypothesis =
                    "Specific, explainable knowledge groups (recent form, usage/opportunity, role/health) " +
                    "can improve Start/Sit decision quality on unseen historical data when applied through " +
                    "explicit bounded transforms — without changing Projection V2 or Confidence V2.",
                SuccessCriteriaText = KnowledgeImpactSuccessCriteria.Text,
                DevelopmentSeasons = developmentSeasons,
                HoldoutSeason = holdout,
                UsedHoldoutDuringFitting = false,
                ProjectionV2Unchanged = true,
                ConfidenceV2Unchanged = true,
                DecisionPolicyV1Unchanged = true,
                AvailableSignalNotes =
                [
                    "PLAYER FORM: RecentProductionScore (populated from prior REG games).",
                    "USAGE: UsageScore + OpportunityScore (reconstructed features).",
                    "HEALTH: InjuryStatus sparse; HealthLabel Healthy default common.",
                    "ROLE: RoleNote from prior-week depth chart (high coverage).",
                    "TEAM/MATCHUP: aspects marked unavailable historically — not experimented.",
                    "CONTEXT weather/rest/home-away: unavailable historically — not experimented."
                ],
                DataCoverageNotes =
                [
                    "nflverse path: news always unavailable; target/snap share often unavailable.",
                    "Matchup Experiment D skipped — OpponentTeam null; team/matchup markers only.",
                    "Quick Picks: no historical prop-line archive / settled-stat grader in-repo."
                ],
                LooFoldSummaries = looSummaries,
                FrozenGroups = selected,
                DevelopmentBaseline = developmentBaseline,
                DevelopmentEnhanced = developmentEnhanced,
                HoldoutBaseline = holdoutBaseline,
                HoldoutEnhanced = holdoutEnhanced,
                AblationRows = holdoutAblation,
                FailureAnalysisNotes = failureNotes,
                QuickPicksEvaluationNote =
                    "Quick Picks consumes Shared Knowledge and Knowledge Impact transforms for live ranking " +
                    "(bounded OpportunityScore deltas). Historical Quick Picks evaluation is NOT available: " +
                    "missing archived prop/closing lines and settled counting-stat outcomes joined at cutoff. " +
                    "Do not claim Quick Picks predictive improvement from this experiment.",
                Verdict = verdict.Verdict,
                VerdictRationale = verdict.Rationale
            };
        }
        finally
        {
            projectionState.PrimaryMode = previousProjection;
            policyState.Mode = previousPolicy;
            knowledgeState.Mode = previousKnowledgeMode;
            knowledgeState.ActiveGroups = previousGroups;
        }
    }

    private async Task<List<SeasonScorecard>> RunSeasonsAsync(
        IMultiWeekHistoricalReplayRunner seasonRunner,
        IHistoricalSeasonCalendar calendar,
        IReadOnlyList<int> seasons,
        KnowledgeMode mode,
        KnowledgeImpactGroup groups,
        KnowledgeImpactExperimentState knowledgeState,
        CancellationToken cancellationToken)
    {
        if (mode == KnowledgeMode.Baseline)
        {
            knowledgeState.ConfigureBaseline();
        }
        else if (mode == KnowledgeMode.Enhanced)
        {
            knowledgeState.ConfigureEnhanced(groups);
        }
        else
        {
            knowledgeState.ConfigurePassthrough();
        }

        var cards = new List<SeasonScorecard>();
        foreach (var season in seasons)
        {
            var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "KnowledgeImpact: season {Season} mode={Mode} groups={Groups}",
                season, mode, groups);
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

    private static KnowledgeImpactGroup[] SupportedGroups() =>
    [
        KnowledgeImpactGroup.RecentForm,
        KnowledgeImpactGroup.Usage,
        KnowledgeImpactGroup.RoleHealth
    ];

    private static (KnowledgeImpactGroup Group, string Name, string Coverage)[] GroupSpecs() =>
    [
        (KnowledgeImpactGroup.RecentForm, "RecentForm",
            "RecentProductionScore / SignalType.RecentProduction — high coverage when prior games exist."),
        (KnowledgeImpactGroup.Usage, "Usage",
            "UsageScore + OpportunityScore — high coverage on reconstructed features."),
        (KnowledgeImpactGroup.RoleHealth, "RoleHealth",
            "RoleNote high coverage; injury designations sparse; Health Healthy default common.")
    ];

    private static KnowledgeImpactGroup SelectGroups(IReadOnlyDictionary<KnowledgeImpactGroup, double> meanVal)
    {
        var selected = KnowledgeImpactGroup.None;
        foreach (var (group, delta) in meanVal)
        {
            if (delta > 0)
            {
                selected |= group;
            }
        }

        if (selected == KnowledgeImpactGroup.None)
        {
            selected = meanVal.OrderByDescending(kv => kv.Value).First().Key;
        }

        return selected;
    }

    private static double TotalDecisionValue(IEnumerable<SeasonScorecard> cards) =>
        cards.SelectMany(c => c.AllGrades)
            .Where(g => g.ActualDecisionDifferential is not null)
            .Sum(g => g.ActualDecisionDifferential!.Value);

    private static KnowledgeImpactScopeMetrics BuildScopeMetrics(
        string label,
        KnowledgeMode mode,
        KnowledgeImpactGroup groups,
        IReadOnlyList<SeasonScorecard> cards,
        IReadOnlyList<SeasonScorecard> baselineCards)
    {
        var grades = cards.SelectMany(c => c.AllGrades).Where(g => g.WasCorrect is not null).ToList();
        var baseGrades = baselineCards.SelectMany(c => c.AllGrades).Where(g => g.WasCorrect is not null).ToList();
        var diffs = grades
            .Where(g => g.ActualDecisionDifferential is not null)
            .Select(g => g.ActualDecisionDifferential!.Value)
            .OrderBy(v => v)
            .ToList();

        var changed = CountChangedDecisions(baseGrades, grades);
        var mae = cards.Select(c => c.CurrentModelMae).Where(v => v is not null).Select(v => v!.Value).ToList();
        var bias = cards.Select(c => c.CurrentModelSignedBias).Where(v => v is not null).Select(v => v!.Value).ToList();

        return new KnowledgeImpactScopeMetrics
        {
            Label = label,
            Mode = mode,
            Groups = groups,
            GradedDecisions = grades.Count,
            DecisionsChangedVsBaseline = changed,
            ChangeRatePercent = baseGrades.Count == 0
                ? null
                : Math.Round(100.0 * changed / baseGrades.Count, 1),
            AccuracyPercent = grades.Count == 0
                ? null
                : Math.Round(100.0 * grades.Count(g => g.WasCorrect == true) / grades.Count, 1),
            AverageDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Average(), 2),
            TotalDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Sum(), 2),
            WorstDecisionCost = diffs.Count == 0 ? null : Math.Round(diffs.First(), 2),
            ProjectionMae = mae.Count == 0 ? null : Math.Round(mae.Average(), 2),
            ProjectionBias = bias.Count == 0 ? null : Math.Round(bias.Average(), 2)
        };
    }

    private static int CountChangedDecisions(
        IReadOnlyList<ReplayDecisionGrade> baseline,
        IReadOnlyList<ReplayDecisionGrade> experiment)
    {
        static string Key(ReplayDecisionGrade g) =>
            $"{g.Season}:{g.Week}:{g.PlayerId}:{g.Recommendation}";

        var baseKeys = baseline.Select(Key).ToHashSet(StringComparer.Ordinal);
        var expKeys = experiment.Select(Key).ToHashSet(StringComparer.Ordinal);
        return baseKeys.Except(expKeys).Count() + expKeys.Except(baseKeys).Count();
    }

    private static IReadOnlyList<string> BuildFailureAnalysis(
        IReadOnlyList<SeasonScorecard> baselineCards,
        IReadOnlyList<SeasonScorecard> enhancedCards,
        KnowledgeImpactScopeMetrics baseline,
        KnowledgeImpactScopeMetrics enhanced)
    {
        var notes = new List<string>
        {
            $"Holdout total decision value baseline={baseline.TotalDecisionValue:0.00} " +
            $"enhanced={enhanced.TotalDecisionValue:0.00} " +
            $"Δ={(enhanced.TotalDecisionValue ?? 0) - (baseline.TotalDecisionValue ?? 0):0.00}.",
            $"Holdout decisions changed vs baseline: {enhanced.DecisionsChangedVsBaseline} " +
            $"({enhanced.ChangeRatePercent:0.#}%)."
        };

        var baseGrades = baselineCards.SelectMany(c => c.AllGrades).Where(g => g.WasCorrect is not null).ToList();
        var enhGrades = enhancedCards.SelectMany(c => c.AllGrades).Where(g => g.WasCorrect is not null).ToList();

        static string Id(ReplayDecisionGrade g) => $"{g.Season}:{g.Week}:{g.PlayerId}";
        var baseById = baseGrades.GroupBy(Id).ToDictionary(g => g.Key, g => g.First());
        var enhById = enhGrades.GroupBy(Id).ToDictionary(g => g.Key, g => g.First());

        var flipped = new List<(ReplayDecisionGrade B, ReplayDecisionGrade E)>();
        foreach (var (id, b) in baseById)
        {
            if (!enhById.TryGetValue(id, out var e))
            {
                continue;
            }

            if (b.Recommendation != e.Recommendation ||
                Math.Abs((b.ActualDecisionDifferential ?? 0) - (e.ActualDecisionDifferential ?? 0)) > 0.05)
            {
                flipped.Add((b, e));
            }
        }

        if (flipped.Count > 0)
        {
            var byPos = flipped.GroupBy(x => x.B.Position)
                .Select(g =>
                {
                    var delta = g.Sum(x =>
                        (x.E.ActualDecisionDifferential ?? 0) - (x.B.ActualDecisionDifferential ?? 0));
                    return $"{g.Key}:n={g.Count()} Δval={delta:0.0}";
                });
            notes.Add("Changed/compared decisions by position: " + string.Join("; ", byPos));

            var helped = flipped.Count(x =>
                (x.E.ActualDecisionDifferential ?? 0) > (x.B.ActualDecisionDifferential ?? 0) + 0.05);
            var hurt = flipped.Count(x =>
                (x.E.ActualDecisionDifferential ?? 0) + 0.05 < (x.B.ActualDecisionDifferential ?? 0));
            notes.Add($"Among compared player-weeks: {helped} improved decision value; {hurt} worsened.");
        }
        else
        {
            notes.Add("No player-week recommendation/value shifts detected vs baseline on holdout.");
        }

        return notes;
    }

    private static ProjectionExperimentVerdict JudgeGroupHoldout(
        KnowledgeImpactAblationRow row,
        KnowledgeImpactScopeMetrics? hold,
        double meanDevDelta)
    {
        if (hold?.TotalDecisionValue is null)
        {
            return row.Verdict;
        }

        // Holdout fill is informational for constituents; primary verdict uses frozen set.
        var holdDelta = hold.TotalDecisionValue.Value;
        // Compared later against baseline in report text; keep development-oriented label.
        _ = holdDelta;
        _ = meanDevDelta;
        return row.Verdict;
    }

    private static string HoldoutGroupRationale(
        KnowledgeImpactAblationRow row,
        KnowledgeImpactScopeMetrics? hold,
        double meanDevDelta)
    {
        if (hold?.TotalDecisionValue is null)
        {
            return row.VerdictRationale + " Holdout single-group run not required for this group.";
        }

        return row.VerdictRationale +
               $" Holdout single-group tot={hold.TotalDecisionValue:0.00} " +
               $"(dev mean Δ={meanDevDelta:0.00}).";
    }

    private static void AssertFrozenLayersUnchanged()
    {
        if (FrozenProjectionCalibrationV2.Method != ProjectionCalibrationMethod.PiecewiseScaleAt20 ||
            Math.Abs(FrozenProjectionCalibrationV2.HighSlope - 0.6005) > 1e-9 ||
            Math.Abs(FrozenProjectionCalibrationV2.LowSlope - 0.9240) > 1e-9)
        {
            throw new InvalidOperationException("Projection V2 frozen parameters were modified.");
        }

        if (!FrozenDecisionConfidenceCalibrationV2.BinStarts.SequenceEqual(new[] { 0, 15, 25, 35 }) ||
            !FrozenDecisionConfidenceCalibrationV2.CalibratedRates.SequenceEqual(new[] { 57, 67, 65, 42 }))
        {
            throw new InvalidOperationException("Confidence V2 frozen mapping was modified.");
        }

        if (FrozenConfidenceAwareDecisionPolicyV1.Kind != DecisionPolicyKinds.SuppressStartAndSit ||
            FrozenConfidenceAwareDecisionPolicyV1.MaxCalibratedConfidenceToSuppressStart != 45 ||
            Math.Abs(FrozenConfidenceAwareDecisionPolicyV1.MaxDecisionValueMarginToSuppress - 6.0) > 1e-9)
        {
            throw new InvalidOperationException("Decision Policy V1 frozen constants were modified.");
        }
    }

    private static void AssertFrozenMatchesSelected(KnowledgeImpactGroup selected)
    {
        if (selected != FrozenKnowledgeImpactExperimentV1.FrozenEnhancedGroups)
        {
            throw new InvalidOperationException(
                "Frozen knowledge-impact groups do not match development selection. " +
                $"Selected={selected} frozen={FrozenKnowledgeImpactExperimentV1.FrozenEnhancedGroups}.");
        }
    }

    private static (ProjectionExperimentVerdict Verdict, string Rationale) Judge(
        KnowledgeImpactScopeMetrics devEnh,
        KnowledgeImpactScopeMetrics devBase,
        KnowledgeImpactScopeMetrics holdEnh,
        KnowledgeImpactScopeMetrics holdBase,
        IReadOnlyDictionary<KnowledgeImpactGroup, double> meanValDeltaByGroup,
        KnowledgeImpactGroup selected)
    {
        var holdDelta = (holdEnh.TotalDecisionValue ?? 0) - (holdBase.TotalDecisionValue ?? 0);
        var devDelta = (devEnh.TotalDecisionValue ?? 0) - (devBase.TotalDecisionValue ?? 0);
        var changeRate = (holdEnh.ChangeRatePercent ?? 0) / 100.0;
        var meanLoo = meanValDeltaByGroup
            .Where(kv => selected.HasFlag(kv.Key))
            .Select(kv => kv.Value)
            .DefaultIfEmpty(0)
            .Average();

        if (changeRate < KnowledgeImpactSuccessCriteria.MinHoldoutChangeRate && Math.Abs(holdDelta) < 5)
        {
            return (
                ProjectionExperimentVerdict.NoMaterialImprovement,
                $"Holdout change rate {changeRate:0%} and Δ={holdDelta:0.00} are not material.");
        }

        if (holdDelta <= -KnowledgeImpactSuccessCriteria.MinHoldoutTotalValueImprovement)
        {
            return (
                ProjectionExperimentVerdict.Regression,
                $"Holdout total decision value worsened by {holdDelta:0.00}. Reject knowledge enhancement.");
        }

        var holdOk = holdDelta >= KnowledgeImpactSuccessCriteria.MinHoldoutTotalValueImprovement;
        var devOk = meanLoo >= KnowledgeImpactSuccessCriteria.MinDevMeanTotalValueImprovement ||
                    devDelta >= KnowledgeImpactSuccessCriteria.MinDevMeanTotalValueImprovement;
        var changeOk = changeRate >= KnowledgeImpactSuccessCriteria.MinHoldoutChangeRate;

        if (holdOk && devOk && changeOk)
        {
            return (
                ProjectionExperimentVerdict.Improvement,
                $"Dev mean/pooled Δ≈{Math.Max(meanLoo, devDelta):0.00}; holdout Δ={holdDelta:0.00}; " +
                $"change rate={changeRate:0%}. Accept frozen groups {selected}.");
        }

        if (Math.Abs(holdDelta) < 5)
        {
            return (
                ProjectionExperimentVerdict.NoMaterialImprovement,
                $"Holdout Δ={holdDelta:0.00} is a tiny fluctuation (dev Δ={devDelta:0.00}).");
        }

        return (
            ProjectionExperimentVerdict.Inconclusive,
            $"Criteria not fully met (dev Δ={devDelta:0.00}, loo mean={meanLoo:0.00}, " +
            $"holdout Δ={holdDelta:0.00}, change={changeRate:0%}). Groups={selected}.");
    }
}
