using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playbook.Application.Predictions;
using Playbook.Application.Replay;
using Playbook.Core.Knowledge;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;

namespace Playbook.Infrastructure.Knowledge;

/// <summary>
/// Baseline vs Passthrough on ExpandedSkillUniverse.
/// No rejected Enhanced transforms. No 2024 during development.
/// </summary>
public sealed class SharedKnowledgeExpandedUniverseExperimentRunner
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SharedKnowledgeExpandedUniverseExperimentRunner> _logger;

    public SharedKnowledgeExpandedUniverseExperimentRunner(
        IServiceProvider services,
        ILogger<SharedKnowledgeExpandedUniverseExperimentRunner> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task<SharedKnowledgeExpandedUniverseExperimentReport> RunOfficialExperimentAsync(
        CancellationToken cancellationToken = default)
    {
        var developmentSeasons = FrozenSharedKnowledgeExpandedUniverseExperimentV1.DevelopmentSeasons.ToList();
        var holdout = FrozenSharedKnowledgeExpandedUniverseExperimentV1.HoldoutSeason;
        var universe = FrozenSharedKnowledgeExpandedUniverseExperimentV1.CandidateUniverse;

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
            AssertRejectedTransformsStayOff();

            var seasonRunner = _services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
            var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();

            _logger.LogInformation("SharedKnowledgeExpanded: development BASELINE");
            var baselineDev = await RunSeasonsAsync(
                    seasonRunner, calendar, developmentSeasons,
                    KnowledgeMode.Baseline, knowledgeState, universe, cancellationToken)
                .ConfigureAwait(false);

            if (baselineDev.Any(c => c.Season == holdout))
            {
                throw new InvalidOperationException("Holdout leaked into shared-knowledge expanded development set.");
            }

            _logger.LogInformation("SharedKnowledgeExpanded: development PASSTHROUGH");
            var treatmentDev = await RunSeasonsAsync(
                    seasonRunner, calendar, developmentSeasons,
                    KnowledgeMode.Passthrough, knowledgeState, universe, cancellationToken)
                .ConfigureAwait(false);

            var looSummaries = new List<string>();
            var foldDeltas = new List<double>();
            foreach (var season in developmentSeasons)
            {
                var baseTot = TotalDecisionValue(baselineDev.Where(c => c.Season == season));
                var treatTot = TotalDecisionValue(treatmentDev.Where(c => c.Season == season));
                var delta = treatTot - baseTot;
                foldDeltas.Add(delta);
                looSummaries.Add(
                    $"Passthrough vs Baseline val={season}: baseTot={baseTot:0.00} " +
                    $"treatTot={treatTot:0.00} Δ={delta:0.00}");
            }

            var meanDevDelta = foldDeltas.Average();
            looSummaries.Add(
                $"FROZEN (no parameter selection): Control=Baseline Treatment=Passthrough " +
                $"Groups=None Universe={universe} meanDevΔ={meanDevDelta:0.00}");

            // Determinism checks on first development season.
            var basRepeat = await RunSeasonsAsync(
                    seasonRunner, calendar, [developmentSeasons[0]],
                    KnowledgeMode.Baseline, knowledgeState, universe, cancellationToken)
                .ConfigureAwait(false);
            AssertSeasonCardDeterministic(baselineDev[0], basRepeat[0], "Baseline Expanded");

            var treatRepeat = await RunSeasonsAsync(
                    seasonRunner, calendar, [developmentSeasons[0]],
                    KnowledgeMode.Passthrough, knowledgeState, universe, cancellationToken)
                .ConfigureAwait(false);
            AssertSeasonCardDeterministic(treatmentDev[0], treatRepeat[0], "Passthrough Expanded");

            var developmentBaseline = BuildScopeMetrics(
                "DEV BASELINE", KnowledgeMode.Baseline, baselineDev, baselineDev);
            var developmentTreatment = BuildScopeMetrics(
                "DEV PASSTHROUGH", KnowledgeMode.Passthrough, treatmentDev, baselineDev);

            var developmentCoverage = await MeasureCoverageAsync(
                    developmentSeasons, universe, cancellationToken)
                .ConfigureAwait(false);
            var developmentCandidates = await CountStartSitCandidatesAsync(
                    developmentSeasons, universe, cancellationToken)
                .ConfigureAwait(false);
            var developmentCategories = BuildCategorySlices(baselineDev, treatmentDev);

            AssertFrozenLayersUnchanged();

            _logger.LogInformation("SharedKnowledgeExpanded: official holdout {Season} BASELINE", holdout);
            var holdoutBaselineCards = await RunSeasonsAsync(
                    seasonRunner, calendar, [holdout],
                    KnowledgeMode.Baseline, knowledgeState, universe, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("SharedKnowledgeExpanded: official holdout {Season} PASSTHROUGH", holdout);
            var holdoutTreatmentCards = await RunSeasonsAsync(
                    seasonRunner, calendar, [holdout],
                    KnowledgeMode.Passthrough, knowledgeState, universe, cancellationToken)
                .ConfigureAwait(false);

            var holdoutBaseline = BuildScopeMetrics(
                "HOLDOUT BASELINE", KnowledgeMode.Baseline, holdoutBaselineCards, holdoutBaselineCards);
            var holdoutTreatment = BuildScopeMetrics(
                "HOLDOUT PASSTHROUGH", KnowledgeMode.Passthrough, holdoutTreatmentCards, holdoutBaselineCards);

            var holdoutCoverage = await MeasureCoverageAsync([holdout], universe, cancellationToken)
                .ConfigureAwait(false);
            var holdoutCandidates = await CountStartSitCandidatesAsync([holdout], universe, cancellationToken)
                .ConfigureAwait(false);
            var holdoutCategories = BuildCategorySlices(holdoutBaselineCards, holdoutTreatmentCards);

            var (qpDevBase, qpDevTreat, qpHoldBase, qpHoldTreat, qpNote) =
                await MeasureQuickPicksAsync(developmentSeasons, holdout, universe, cancellationToken)
                    .ConfigureAwait(false);

            var failureNotes = BuildFailureAnalysis(
                holdoutBaselineCards, holdoutTreatmentCards, holdoutBaseline, holdoutTreatment);
            failureNotes = failureNotes
                .Append(FrozenSharedKnowledgeExpandedUniverseExperimentV1.MappingSummary)
                .Append(qpNote)
                .Append(
                    $"Dev mean Δ total decision value={meanDevDelta:0.00} " +
                    $"(informational; no candidate search).")
                .ToList();

            var holdDelta = (holdoutTreatment.TotalDecisionValue ?? 0) - (holdoutBaseline.TotalDecisionValue ?? 0);
            var changeRate = (holdoutTreatment.ChangeRatePercent ?? 0) / 100.0;
            var (verdict, rationale) = Judge(holdDelta, changeRate, meanDevDelta);

            return new SharedKnowledgeExpandedUniverseExperimentReport
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                ExperimentId = FrozenSharedKnowledgeExpandedUniverseExperimentV1.ExperimentId,
                Hypothesis = FrozenSharedKnowledgeExpandedUniverseExperimentV1.Hypothesis,
                SuccessCriteriaText = KnowledgeImpactSuccessCriteria.Text,
                DevelopmentSeasons = developmentSeasons,
                HoldoutSeason = holdout,
                CandidateUniverse = universe,
                UsedHoldoutDuringFitting = false,
                ProjectionV2Unchanged = true,
                ConfidenceV2Unchanged = true,
                DecisionPolicyV1Unchanged = true,
                RejectedTransformsRemainDisabled = true,
                LooFoldSummaries = looSummaries,
                DevelopmentBaseline = developmentBaseline,
                DevelopmentTreatment = developmentTreatment,
                HoldoutBaseline = holdoutBaseline,
                HoldoutTreatment = holdoutTreatment,
                DevelopmentStartSitCandidates = developmentCandidates,
                HoldoutStartSitCandidates = holdoutCandidates,
                DevelopmentCoverage = developmentCoverage,
                HoldoutCoverage = holdoutCoverage,
                DevelopmentCategorySlices = developmentCategories,
                HoldoutCategorySlices = holdoutCategories,
                HoldoutBaselineConfidenceBuckets = BuildConfidenceBuckets(holdoutBaselineCards),
                HoldoutTreatmentConfidenceBuckets = BuildConfidenceBuckets(holdoutTreatmentCards),
                DevelopmentQuickPicksBaseline = qpDevBase,
                DevelopmentQuickPicksTreatment = qpDevTreat,
                HoldoutQuickPicksBaseline = qpHoldBase,
                HoldoutQuickPicksTreatment = qpHoldTreat,
                FailureAnalysisNotes = failureNotes,
                Verdict = verdict,
                VerdictRationale = rationale
            };
        }
        finally
        {
            projectionState.PrimaryMode = previousProjection;
            policyState.Mode = previousPolicy;
            knowledgeState.Mode = previousKnowledgeMode;
            knowledgeState.ActiveGroups = previousGroups;
            knowledgeState.ConfigurePassthrough();
        }
    }

    private async Task<List<SeasonScorecard>> RunSeasonsAsync(
        IMultiWeekHistoricalReplayRunner seasonRunner,
        IHistoricalSeasonCalendar calendar,
        IReadOnlyList<int> seasons,
        KnowledgeMode mode,
        KnowledgeImpactExperimentState knowledgeState,
        HistoricalCandidateUniverse universe,
        CancellationToken cancellationToken)
    {
        if (mode == KnowledgeMode.Baseline)
        {
            knowledgeState.ConfigureBaseline();
        }
        else
        {
            knowledgeState.ConfigurePassthrough();
        }

        knowledgeState.ActiveGroups = KnowledgeImpactGroup.None;

        var cards = new List<SeasonScorecard>();
        foreach (var season in seasons)
        {
            var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "SharedKnowledgeExpanded: season {Season} mode={Mode} universe={Universe}",
                season, mode, universe);
            cards.Add(await seasonRunner.RunAsync(
                    new MultiWeekReplayRequest
                    {
                        Season = season,
                        StartWeek = 1,
                        EndWeek = end,
                        FixtureId = "nflverse",
                        ContinueOnWeekFailure = true,
                        CandidateUniverse = universe
                    },
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return cards;
    }

    private async Task<(
            SharedKnowledgeQuickPickScope DevBase,
            SharedKnowledgeQuickPickScope DevTreat,
            SharedKnowledgeQuickPickScope HoldBase,
            SharedKnowledgeQuickPickScope HoldTreat,
            string Note)>
        MeasureQuickPicksAsync(
            IReadOnlyList<int> developmentSeasons,
            int holdout,
            HistoricalCandidateUniverse universe,
            CancellationToken cancellationToken)
    {
        var qp = _services.GetRequiredService<IQuickPicksHistoricalEvaluationRunner>();
        var knowledgeState = _services.GetRequiredService<KnowledgeImpactExperimentState>();
        var previousMode = knowledgeState.Mode;
        var previousGroups = knowledgeState.ActiveGroups;

        try
        {
            // Control: Baseline QuickPickMode under Baseline knowledge mode.
            knowledgeState.ConfigureBaseline();
            var basDevCards = new List<QuickPickSeasonScorecard>();
            foreach (var season in developmentSeasons)
            {
                basDevCards.Add(await qp.RunSeasonAsync(
                        season,
                        QuickPickMode.Baseline,
                        fixtureId: "nflverse",
                        candidateUniverse: universe,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false));
            }

            var basHold = await qp.RunSeasonAsync(
                    holdout,
                    QuickPickMode.Baseline,
                    fixtureId: "nflverse",
                    candidateUniverse: universe,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // Treatment: Enhanced QuickPickMode with ActiveGroups=None.
            // Attaches PredictionContext; applicator is ranking-identity (no rejected transforms).
            var treatDevCards = new List<QuickPickSeasonScorecard>();
            foreach (var season in developmentSeasons)
            {
                treatDevCards.Add(await qp.RunSeasonAsync(
                        season,
                        QuickPickMode.Enhanced,
                        fixtureId: "nflverse",
                        enhancedGroups: KnowledgeImpactGroup.None,
                        candidateUniverse: universe,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false));
            }

            var treatHold = await qp.RunSeasonAsync(
                    holdout,
                    QuickPickMode.Enhanced,
                    fixtureId: "nflverse",
                    enhancedGroups: KnowledgeImpactGroup.None,
                    candidateUniverse: universe,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var devBase = AggregateQp("DEV QP BASELINE", basDevCards, basDevCards);
            var devTreat = AggregateQp("DEV QP PASSTHROUGH-ATTACHED", treatDevCards, basDevCards);
            var holdBase = AggregateQp("HOLDOUT QP BASELINE", [basHold], [basHold]);
            var holdTreat = AggregateQp("HOLDOUT QP PASSTHROUGH-ATTACHED", [treatHold], [basHold]);

            var note =
                $"Quick Picks ExpandedSkillUniverse: " +
                $"holdout MAE {holdBase.MeanAbsoluteError:0.000}→{holdTreat.MeanAbsoluteError:0.000}; " +
                $"Top5 {holdBase.Top5HitRate:0.0}%→{holdTreat.Top5HitRate:0.0}%; " +
                $"ranksChanged={holdTreat.RanksChangedVsControl} " +
                $"({holdTreat.RankChangeRatePercent:0.#}%). " +
                "Enhanced+None attaches knowledge with ranking identity (no rejected transforms).";

            return (devBase, devTreat, holdBase, holdTreat, note);
        }
        finally
        {
            knowledgeState.Mode = previousMode;
            knowledgeState.ActiveGroups = previousGroups;
            knowledgeState.ConfigurePassthrough();
        }
    }

    private async Task<SharedKnowledgeCoverageStats> MeasureCoverageAsync(
        IReadOnlyList<int> seasons,
        HistoricalCandidateUniverse universe,
        CancellationToken cancellationToken)
    {
        var source = _services.GetRequiredService<IHistoricalSnapshotSource>();
        var builder = _services.GetRequiredService<IHistoricalSnapshotBuilder>();
        var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();

        var candidates = 0;
        var validProj = 0;
        var usable = 0;
        var opp = 0;
        var usage = 0;
        var form = 0;
        var role = 0;
        var health = 0;
        var suff = 0;
        var lim = 0;
        var insuff = 0;

        foreach (var season in seasons)
        {
            var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
                .ConfigureAwait(false);
            for (var week = 1; week <= end; week++)
            {
                var raw = await source
                    .GetRawWeekAsync(season, week, Core.Leagues.ScoringType.Ppr, "nflverse", universe, cancellationToken)
                    .ConfigureAwait(false);
                if (raw is null)
                {
                    continue;
                }

                var (snapshot, _) = builder.Build(raw);
                foreach (var player in snapshot.Players)
                {
                    candidates++;
                    if (player.ProjectedPoints is not null)
                    {
                        validProj++;
                    }

                    if (player.OpportunityScore is not null)
                    {
                        opp++;
                    }

                    if (player.UsageScore is not null)
                    {
                        usage++;
                    }

                    if (player.RecentProductionScore is not null)
                    {
                        form++;
                    }

                    if (!string.IsNullOrWhiteSpace(player.RoleNote))
                    {
                        role++;
                    }

                    if (!string.IsNullOrWhiteSpace(player.HealthLabel) &&
                        !player.HealthLabel.Contains("Healthy", StringComparison.OrdinalIgnoreCase))
                    {
                        health++;
                    }

                    switch (player.DataSufficiency)
                    {
                        case DataSufficiency.Sufficient:
                            suff++;
                            break;
                        case DataSufficiency.Limited:
                            lim++;
                            break;
                        case DataSufficiency.Insufficient:
                            insuff++;
                            break;
                    }

                    var hasUsable =
                        player.OpportunityScore is not null ||
                        player.UsageScore is not null ||
                        player.RecentProductionScore is not null ||
                        !string.IsNullOrWhiteSpace(player.RoleNote) ||
                        (player.HealthLabel is not null &&
                         !player.HealthLabel.Contains("Healthy", StringComparison.OrdinalIgnoreCase));
                    if (hasUsable)
                    {
                        usable++;
                    }
                }
            }
        }

        var projectionOnly = Math.Max(0, candidates - usable);
        return new SharedKnowledgeCoverageStats
        {
            CandidatePlayerWeeks = candidates,
            WithValidProjection = validProj,
            WithUsableSharedKnowledge = usable,
            ProjectionOnlyOrUnknown = projectionOnly,
            WithOpportunity = opp,
            WithUsage = usage,
            WithRecentProduction = form,
            WithRole = role,
            WithNonDefaultHealth = health,
            WithLimitedHistory = lim,
            WithInsufficientHistory = insuff,
            WithSufficientHistory = suff,
            UsableKnowledgeRatePercent = candidates == 0
                ? 0
                : Math.Round(100.0 * usable / candidates, 1),
            UnavailabilityNotes =
            [
                "Matchup/team environment aspects: UNAVAILABLE historically.",
                "News archive: UNAVAILABLE — not fabricated.",
                "Real fantasy league ownership: UNAVAILABLE.",
                "Prop lines / as-of vendor projections: UNAVAILABLE.",
                "Baseline strips Opportunity/Usage/RecentProduction/Role/Health even when present.",
                "Passthrough keeps assembled knowledge; Enhanced rejected transforms remain off."
            ]
        };
    }

    private async Task<int> CountStartSitCandidatesAsync(
        IReadOnlyList<int> seasons,
        HistoricalCandidateUniverse universe,
        CancellationToken cancellationToken)
    {
        var source = _services.GetRequiredService<IHistoricalSnapshotSource>();
        var calendar = _services.GetRequiredService<IHistoricalSeasonCalendar>();
        var n = 0;
        foreach (var season in seasons)
        {
            var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
                .ConfigureAwait(false);
            for (var week = 1; week <= end; week++)
            {
                var raw = await source
                    .GetRawWeekAsync(season, week, Core.Leagues.ScoringType.Ppr, "nflverse", universe, cancellationToken)
                    .ConfigureAwait(false);
                if (raw is null)
                {
                    continue;
                }

                n += raw.Roster.Count;
            }
        }

        return n;
    }

    private static SharedKnowledgeQuickPickScope AggregateQp(
        string label,
        IReadOnlyList<QuickPickSeasonScorecard> cards,
        IReadOnlyList<QuickPickSeasonScorecard> controlCards)
    {
        var graded = cards.SelectMany(c => c.Graded).ToList();
        var control = controlCards.SelectMany(c => c.Graded).ToList();
        var changed = CountRankChanges(control, graded);
        var preds = graded.Count;
        var mae = graded.Count == 0 ? 0 : graded.Average(g => g.AbsoluteError);
        var conf = graded
            .Where(g => g.Prediction.Confidence is not null)
            .Select(g => (double)g.Prediction.Confidence!.Value)
            .ToList();

        // Top-N / rank MAE: prefer scorecard aggregates when single card; else recompute lightly.
        // Scorecard TopN rates are stored as 0–100 percentages.
        double top5;
        double top10;
        double rankMae;
        if (cards.Count == 1)
        {
            top5 = cards[0].Top5HitRate;
            top10 = cards[0].Top10HitRate;
            rankMae = cards[0].MeanRankAbsoluteError;
            mae = cards[0].MeanAbsoluteError;
            preds = cards[0].PredictionsEvaluated;
        }
        else
        {
            top5 = cards.Average(c => c.Top5HitRate);
            top10 = cards.Average(c => c.Top10HitRate);
            rankMae = cards.Average(c => c.MeanRankAbsoluteError);
            mae = cards.Average(c => c.MeanAbsoluteError);
            preds = cards.Sum(c => c.PredictionsEvaluated);
        }

        return new SharedKnowledgeQuickPickScope
        {
            Label = label,
            PredictionsEvaluated = preds,
            MeanAbsoluteError = Math.Round(mae, 3),
            Top5HitRate = top5,
            Top10HitRate = top10,
            MeanRankAbsoluteError = Math.Round(rankMae, 2),
            AverageConfidence = conf.Count == 0 ? null : Math.Round(conf.Average(), 1),
            RanksChangedVsControl = changed,
            RankChangeRatePercent = control.Count == 0
                ? null
                : Math.Round(100.0 * changed / control.Count, 1),
            KnowledgeAttachedCount = graded.Count(g => g.Prediction.KnowledgeAttached)
        };
    }

    private static int CountRankChanges(
        IReadOnlyList<QuickPickGradedPrediction> control,
        IReadOnlyList<QuickPickGradedPrediction> treatment)
    {
        static string Key(QuickPickGradedPrediction g) =>
            $"{g.Prediction.Season}:{g.Prediction.Week}:{g.Prediction.PlayerId}:{g.Prediction.Market}";

        var c = control.ToDictionary(Key, g => g.Prediction.RankInMarket);
        var changed = 0;
        foreach (var t in treatment)
        {
            if (c.TryGetValue(Key(t), out var rank) && rank != t.Prediction.RankInMarket)
            {
                changed++;
            }
        }

        return changed;
    }

    private static IReadOnlyList<SharedKnowledgeCategorySlice> BuildCategorySlices(
        IReadOnlyList<SeasonScorecard> baseline,
        IReadOnlyList<SeasonScorecard> treatment)
    {
        var categories = new[]
        {
            "DataSufficiency:Sufficient",
            "DataSufficiency:Limited",
            "DataSufficiency:Insufficient",
            "DataSufficiency:Unknown"
        };

        return categories.Select(cat =>
        {
            var baseG = FilterByCategory(baseline, cat);
            var treatG = FilterByCategory(treatment, cat);
            return new SharedKnowledgeCategorySlice
            {
                Category = cat,
                BaselineGraded = baseG.Count,
                TreatmentGraded = treatG.Count,
                BaselineTotalDecisionValue = SumDv(baseG),
                TreatmentTotalDecisionValue = SumDv(treatG),
                DeltaTotalDecisionValue = (SumDv(treatG) ?? 0) - (SumDv(baseG) ?? 0),
                BaselineAccuracyPercent = Acc(baseG),
                TreatmentAccuracyPercent = Acc(treatG)
            };
        }).ToList();
    }

    private static List<ReplayDecisionGrade> FilterByCategory(
        IReadOnlyList<SeasonScorecard> cards,
        string category)
    {
        var grades = cards.SelectMany(c => c.AllGrades).Where(g => g.WasCorrect is not null);
        return category switch
        {
            "DataSufficiency:Sufficient" => grades
                .Where(g => g.DataSufficiency == DataSufficiency.Sufficient).ToList(),
            "DataSufficiency:Limited" => grades
                .Where(g => g.DataSufficiency == DataSufficiency.Limited).ToList(),
            "DataSufficiency:Insufficient" => grades
                .Where(g => g.DataSufficiency == DataSufficiency.Insufficient).ToList(),
            _ => grades.Where(g => g.DataSufficiency is null).ToList()
        };
    }

    private static double? SumDv(IReadOnlyList<ReplayDecisionGrade> grades)
    {
        var vals = grades
            .Where(g => g.ActualDecisionDifferential is not null)
            .Select(g => g.ActualDecisionDifferential!.Value)
            .ToList();
        return vals.Count == 0 ? null : Math.Round(vals.Sum(), 2);
    }

    private static double? Acc(IReadOnlyList<ReplayDecisionGrade> grades) =>
        grades.Count == 0
            ? null
            : Math.Round(100.0 * grades.Count(g => g.WasCorrect == true) / grades.Count, 1);

    private static IReadOnlyList<SharedKnowledgeConfidenceBucketRow> BuildConfidenceBuckets(
        IReadOnlyList<SeasonScorecard> cards)
    {
        return cards
            .SelectMany(c => c.ConfidenceBuckets)
            .GroupBy(b => b.Label)
            .Select(g =>
            {
                var n = g.Sum(x => x.DecisionCount);
                var graded = g.Sum(x => x.GradedCount);
                var success = g.Sum(x => x.CorrectCount);
                var avgVals = g.Where(x => x.AverageDecisionValue is not null)
                    .Select(x => x.AverageDecisionValue!.Value)
                    .ToList();
                return new SharedKnowledgeConfidenceBucketRow
                {
                    Label = g.Key,
                    DecisionCount = n,
                    GradedCount = graded,
                    SuccessRatePercent = graded == 0
                        ? null
                        : Math.Round(100.0 * success / graded, 1),
                    AverageDecisionValue = avgVals.Count == 0
                        ? null
                        : Math.Round(avgVals.Average(), 2)
                };
            })
            .OrderBy(b => b.Label)
            .ToList();
    }

    private static KnowledgeImpactScopeMetrics BuildScopeMetrics(
        string label,
        KnowledgeMode mode,
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
            Groups = KnowledgeImpactGroup.None,
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
        IReadOnlyList<SeasonScorecard> treatmentCards,
        KnowledgeImpactScopeMetrics baseline,
        KnowledgeImpactScopeMetrics treatment)
    {
        var notes = new List<string>
        {
            $"Holdout total decision value baseline={baseline.TotalDecisionValue:0.00} " +
            $"passthrough={treatment.TotalDecisionValue:0.00} " +
            $"Δ={(treatment.TotalDecisionValue ?? 0) - (baseline.TotalDecisionValue ?? 0):0.00}.",
            $"Holdout decisions changed vs baseline: {treatment.DecisionsChangedVsBaseline} " +
            $"({treatment.ChangeRatePercent:0.#}%)."
        };

        var baseGrades = baselineCards.SelectMany(c => c.AllGrades).Where(g => g.WasCorrect is not null).ToList();
        var treatGrades = treatmentCards.SelectMany(c => c.AllGrades).Where(g => g.WasCorrect is not null).ToList();
        static string Id(ReplayDecisionGrade g) => $"{g.Season}:{g.Week}:{g.PlayerId}";
        var baseById = baseGrades.GroupBy(Id).ToDictionary(g => g.Key, g => g.First());
        var treatById = treatGrades.GroupBy(Id).ToDictionary(g => g.Key, g => g.First());
        var flipped = new List<(ReplayDecisionGrade B, ReplayDecisionGrade T)>();
        foreach (var (id, b) in baseById)
        {
            if (!treatById.TryGetValue(id, out var t))
            {
                continue;
            }

            if (b.Recommendation != t.Recommendation ||
                Math.Abs((b.ActualDecisionDifferential ?? 0) - (t.ActualDecisionDifferential ?? 0)) > 0.05)
            {
                flipped.Add((b, t));
            }
        }

        if (flipped.Count > 0)
        {
            var byPos = flipped.GroupBy(x => x.B.Position)
                .Select(g =>
                {
                    var delta = g.Sum(x =>
                        (x.T.ActualDecisionDifferential ?? 0) - (x.B.ActualDecisionDifferential ?? 0));
                    return $"{g.Key}:n={g.Count()} Δval={delta:0.0}";
                });
            notes.Add("Changed/compared decisions by position: " + string.Join("; ", byPos));
        }

        return notes;
    }

    private static (ProjectionExperimentVerdict Verdict, string Rationale) Judge(
        double holdDelta,
        double changeRate,
        double meanDevDelta)
    {
        if (changeRate < KnowledgeImpactSuccessCriteria.MinHoldoutChangeRate && Math.Abs(holdDelta) < 5)
        {
            return (
                ProjectionExperimentVerdict.NoMaterialImprovement,
                $"NEUTRAL / DISABLED: holdout Δ={holdDelta:0.00} with change rate {changeRate:0.0%} " +
                $"(need ≥{KnowledgeImpactSuccessCriteria.MinHoldoutChangeRate:0%}). " +
                $"Dev mean Δ={meanDevDelta:0.00}. Rejected transforms remain off; production Passthrough unchanged " +
                "(treatment already equals production knowledge mode — enablement N/A).");
        }

        if (holdDelta <= -KnowledgeImpactSuccessCriteria.MinHoldoutTotalValueImprovement &&
            changeRate >= KnowledgeImpactSuccessCriteria.MinHoldoutChangeRate)
        {
            return (
                ProjectionExperimentVerdict.Regression,
                $"REGRESSION: holdout Δ={holdDelta:0.00} (≤ −{KnowledgeImpactSuccessCriteria.MinHoldoutTotalValueImprovement}) " +
                $"with change {changeRate:0.0%}. Do not treat Passthrough shared knowledge as an improvement " +
                "signal on ExpandedSkillUniverse. Production remains Passthrough (status quo).");
        }

        if (holdDelta >= KnowledgeImpactSuccessCriteria.MinHoldoutTotalValueImprovement &&
            changeRate >= KnowledgeImpactSuccessCriteria.MinHoldoutChangeRate)
        {
            return (
                ProjectionExperimentVerdict.Improvement,
                $"IMPROVEMENT: holdout Δ={holdDelta:0.00} with change {changeRate:0.0%}. " +
                $"Dev mean Δ={meanDevDelta:0.00}. Shared knowledge (Passthrough vs Baseline) helps on " +
                "ExpandedSkillUniverse. Freeze as measurement finding; do not stack another change yet. " +
                "Production already Passthrough — no mode flip required.");
        }

        return (
            ProjectionExperimentVerdict.Inconclusive,
            $"NEUTRAL / INCONCLUSIVE: holdout Δ={holdDelta:0.00}, change {changeRate:0.0%}, " +
            $"dev mean Δ={meanDevDelta:0.00} — does not meet " +
            $"Δ≥{KnowledgeImpactSuccessCriteria.MinHoldoutTotalValueImprovement} and " +
            $"change≥{KnowledgeImpactSuccessCriteria.MinHoldoutChangeRate:0%}. " +
            "Reject enablement claim; production remains Passthrough.");
    }

    private static double TotalDecisionValue(IEnumerable<SeasonScorecard> cards) =>
        cards.SelectMany(c => c.AllGrades)
            .Where(g => g.ActualDecisionDifferential is not null)
            .Sum(g => g.ActualDecisionDifferential!.Value);

    private static void AssertSeasonCardDeterministic(SeasonScorecard a, SeasonScorecard b, string label)
    {
        if (Math.Abs((a.DecisionAccuracyPercent ?? -1) - (b.DecisionAccuracyPercent ?? -1)) > 1e-9 ||
            Math.Abs((a.CurrentModelMae ?? -1) - (b.CurrentModelMae ?? -1)) > 1e-9 ||
            a.AllGrades.Count != b.AllGrades.Count)
        {
            throw new InvalidOperationException($"Non-deterministic season replay ({label}).");
        }
    }

    private static void AssertFrozenLayersUnchanged()
    {
        if (FrozenProjectionCalibrationV2.Method != ProjectionCalibrationMethod.PiecewiseScaleAt20 ||
            Math.Abs(FrozenProjectionCalibrationV2.HighSlope - 0.6005) > 1e-12 ||
            Math.Abs(FrozenProjectionCalibrationV2.LowSlope - 0.9240) > 1e-12)
        {
            throw new InvalidOperationException("Projection V2 constants mutated.");
        }

        if (FrozenConfidenceAwareDecisionPolicyV1.Kind != DecisionPolicyKinds.SuppressStartAndSit)
        {
            throw new InvalidOperationException("Decision Policy V1 constant mutated.");
        }
    }

    private static void AssertRejectedTransformsStayOff()
    {
        if (FrozenSharedKnowledgeExpandedUniverseExperimentV1.ActiveGroups != KnowledgeImpactGroup.None)
        {
            throw new InvalidOperationException("Expanded-universe experiment must keep ActiveGroups=None.");
        }
    }
}
