using Playbook.Core.Decisions;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Calibration;

/// <summary>
/// Development-only leave-one-season-out search for a simple confidence-aware decision policy.
/// Never accepts holdout (2024) observations.
/// </summary>
public static class ConfidenceAwareDecisionPolicyFitter
{
    public static readonly IReadOnlyList<int> CandidateThresholds = [40, 45, 50, 55, 60, 65];

    public static readonly IReadOnlyList<double> CandidateMargins = [3.0, 6.0];

    public static readonly IReadOnlyList<string> CandidateKinds =
    [
        DecisionPolicyKinds.SuppressStart,
        DecisionPolicyKinds.SuppressStartAndSit,
        DecisionPolicyKinds.SwapStart
    ];

    public sealed record SelectionResult(
        DecisionPolicyCandidate Selected,
        IReadOnlyList<string> FoldSummaries,
        double MeanValidationTotalValueDelta,
        double MeanValidationRetention,
        IReadOnlyList<DecisionPolicyCandidate> AllCandidates,
        IReadOnlyDictionary<string, double> MeanValTotalValueByCandidate);

    public static IReadOnlyList<DecisionPolicyCandidate> EnumerateCandidates()
    {
        var list = new List<DecisionPolicyCandidate>();
        foreach (var kind in CandidateKinds)
        {
            foreach (var threshold in CandidateThresholds)
            {
                foreach (var margin in CandidateMargins)
                {
                    list.Add(new DecisionPolicyCandidate
                    {
                        CandidateId = $"{kind}@t{threshold}-m{margin:0}",
                        Kind = kind,
                        Threshold = threshold,
                        Margin = margin,
                        Description =
                            $"{kind}: act when calibratedConfidence <= {threshold} " +
                            $"and DecisionValue margin < {margin:0.#}."
                    });
                }
            }
        }

        return list;
    }

    public static SelectionResult SelectViaLeaveOneSeasonOut(
        IReadOnlyList<DecisionPolicyObservation> developmentObservations,
        IReadOnlyList<int> developmentSeasons,
        int holdoutSeason)
    {
        if (developmentObservations.Any(o => o.Season == holdoutSeason))
        {
            throw new InvalidOperationException(
                $"Holdout season {holdoutSeason} leaked into decision-policy development set.");
        }

        var candidates = EnumerateCandidates();
        var foldSummaries = new List<string>();
        var valDeltaByCandidate = candidates.ToDictionary(c => c.CandidateId, _ => 0.0);
        var valRetentionByCandidate = candidates.ToDictionary(c => c.CandidateId, _ => 0.0);
        var foldWins = candidates.ToDictionary(c => c.CandidateId, _ => 0);
        var foldCount = 0;
        var selectedFoldDeltas = new List<double>();
        var selectedFoldRetentions = new List<double>();

        foreach (var valSeason in developmentSeasons)
        {
            foldCount++;
            var train = developmentObservations.Where(o => o.Season != valSeason).ToList();
            var val = developmentObservations.Where(o => o.Season == valSeason).ToList();
            var trainControl = Evaluate("train-control", train, null);
            var valControl = Evaluate("val-control", val, null);

            // Select on train only.
            var trainRanked = candidates
                .Select(c =>
                {
                    var exp = Evaluate(c.CandidateId, train, c);
                    var delta = (exp.TotalDecisionValue ?? 0) - (trainControl.TotalDecisionValue ?? 0);
                    var retention = trainControl.GradedDecisions == 0
                        ? 1.0
                        : (double)exp.GradedDecisions / trainControl.GradedDecisions;
                    return (Candidate: c, Delta: delta, Retention: retention);
                })
                .Where(x => x.Retention >= ConfidenceAwareDecisionPolicySuccessCriteria.MinHoldoutDecisionRetention)
                .OrderByDescending(x => x.Delta)
                .ThenByDescending(x => x.Retention)
                .ThenBy(x => x.Candidate.Threshold)
                .ThenBy(x => x.Candidate.Margin)
                .ThenBy(x => x.Candidate.Kind, StringComparer.Ordinal)
                .ToList();

            var foldSelected = trainRanked.Count > 0
                ? trainRanked[0].Candidate
                : candidates.First(c =>
                    c.Kind == DecisionPolicyKinds.SuppressStart &&
                    c.Threshold == 45 &&
                    Math.Abs(c.Margin - 6.0) < 1e-9);

            foldWins[foldSelected.CandidateId]++;

            // Validate every candidate on the held-out season (for reporting + robustness).
            foreach (var candidate in candidates)
            {
                var valExp = Evaluate(candidate.CandidateId, val, candidate);
                var delta = (valExp.TotalDecisionValue ?? 0) - (valControl.TotalDecisionValue ?? 0);
                var retention = valControl.GradedDecisions == 0
                    ? 1.0
                    : (double)valExp.GradedDecisions / valControl.GradedDecisions;
                valDeltaByCandidate[candidate.CandidateId] += delta;
                valRetentionByCandidate[candidate.CandidateId] += retention;
            }

            var selectedVal = Evaluate(foldSelected.CandidateId, val, foldSelected);
            var selectedDelta = (selectedVal.TotalDecisionValue ?? 0) - (valControl.TotalDecisionValue ?? 0);
            var selectedRetention = valControl.GradedDecisions == 0
                ? 1.0
                : (double)selectedVal.GradedDecisions / valControl.GradedDecisions;
            selectedFoldDeltas.Add(selectedDelta);
            selectedFoldRetentions.Add(selectedRetention);

            foldSummaries.Add(
                $"train≠{valSeason} → selected={foldSelected.CandidateId}; " +
                $"val controlTot={valControl.TotalDecisionValue:0.00} n={valControl.GradedDecisions}; " +
                $"selectedValΔ={selectedDelta:0.00} ret={selectedRetention:0%}");
        }

        // Final freeze: select on all development observations (same objective).
        var allControl = Evaluate("dev-control", developmentObservations, null);
        var finalRanked = candidates
            .Select(c =>
            {
                var exp = Evaluate(c.CandidateId, developmentObservations, c);
                var delta = (exp.TotalDecisionValue ?? 0) - (allControl.TotalDecisionValue ?? 0);
                var retention = allControl.GradedDecisions == 0
                    ? 1.0
                    : (double)exp.GradedDecisions / allControl.GradedDecisions;
                var meanValDelta = valDeltaByCandidate[c.CandidateId] / foldCount;
                var meanValRetention = valRetentionByCandidate[c.CandidateId] / foldCount;
                return (Candidate: c, DevDelta: delta, DevRetention: retention, MeanValDelta: meanValDelta,
                    MeanValRetention: meanValRetention, Wins: foldWins[c.CandidateId]);
            })
            .Where(x => x.DevRetention >= ConfidenceAwareDecisionPolicySuccessCriteria.MinHoldoutDecisionRetention)
            .OrderByDescending(x => x.MeanValDelta)
            .ThenByDescending(x => x.DevDelta)
            .ThenByDescending(x => x.Wins)
            .ThenBy(x => x.Candidate.Threshold)
            .ThenBy(x => x.Candidate.Margin)
            .ThenBy(x => x.Candidate.Kind, StringComparer.Ordinal)
            .ToList();

        var selected = finalRanked.Count > 0
            ? finalRanked[0]
            : (
                Candidate: candidates.First(c =>
                    c.Kind == DecisionPolicyKinds.SuppressStart &&
                    c.Threshold == 45 &&
                    Math.Abs(c.Margin - 6.0) < 1e-9),
                DevDelta: 0,
                DevRetention: 1,
                MeanValDelta: selectedFoldDeltas.DefaultIfEmpty(0).Average(),
                MeanValRetention: selectedFoldRetentions.DefaultIfEmpty(1).Average(),
                Wins: 0);

        foldSummaries.Add(
            $"finalFreeze={selected.Candidate.CandidateId} meanValΔ={selected.MeanValDelta:0.00} " +
            $"devΔ={selected.DevDelta:0.00} foldWins={selected.Wins}/{foldCount}");

        return new SelectionResult(
            selected.Candidate,
            foldSummaries,
            selected.MeanValDelta,
            selected.MeanValRetention,
            candidates,
            valDeltaByCandidate.ToDictionary(kv => kv.Key, kv => kv.Value / foldCount));
    }

    public static DecisionPolicyScopeMetrics Evaluate(
        string label,
        IReadOnlyList<DecisionPolicyObservation> observations,
        DecisionPolicyCandidate? policy)
    {
        var opportunities = observations.Count;
        if (policy is null)
        {
            return MetricsFromKept(label, observations, opportunities, 0, 0, 0, 0, suppressedValue: null);
        }

        var kept = new List<DecisionPolicyObservation>();
        var suppressedStarts = 0;
        var suppressedSits = 0;
        var swapped = 0;
        var suppressedValue = 0.0;
        var lowTrust = 0;

        foreach (var obs in observations)
        {
            var acts = ShouldAct(obs, policy);
            if (!acts)
            {
                kept.Add(obs);
                if (obs.CalibratedConfidence < FrozenConfidenceAwareDecisionPolicyV1.HighTrustMinCalibratedConfidence)
                {
                    lowTrust++;
                }

                continue;
            }

            if (obs.Recommendation == DecisionRecommendation.Start)
            {
                if (policy.Kind == DecisionPolicyKinds.SwapStart)
                {
                    // Offline swap simulation: flip comparative differential sign.
                    swapped++;
                    kept.Add(CloneFlipped(obs));

                    lowTrust++;
                    continue;
                }

                suppressedStarts++;
                if (obs.ActualDecisionDifferential is double dv)
                {
                    suppressedValue += dv;
                }

                continue;
            }

            if (obs.Recommendation == DecisionRecommendation.Sit &&
                policy.Kind == DecisionPolicyKinds.SuppressStartAndSit)
            {
                suppressedSits++;
                if (obs.ActualDecisionDifferential is double dv)
                {
                    suppressedValue += dv;
                }

                continue;
            }

            kept.Add(obs);
            if (obs.CalibratedConfidence < FrozenConfidenceAwareDecisionPolicyV1.HighTrustMinCalibratedConfidence)
            {
                lowTrust++;
            }
        }

        return MetricsFromKept(
            label,
            kept,
            opportunities,
            suppressedStarts,
            suppressedSits,
            swapped,
            lowTrust,
            suppressedValue);
    }

    public static bool ShouldAct(DecisionPolicyObservation obs, DecisionPolicyCandidate policy)
    {
        if (obs.CalibratedConfidence > policy.Threshold)
        {
            return false;
        }

        var margin = obs.DecisionValueMargin ?? obs.RecommendationMargin;
        if (margin is null)
        {
            return true;
        }

        return margin.Value < policy.Margin;
    }

    private static DecisionPolicyObservation CloneFlipped(DecisionPolicyObservation obs) =>
        new()
        {
            Season = obs.Season,
            Week = obs.Week,
            DecisionId = obs.DecisionId,
            PlayerId = obs.PlayerId,
            PlayerName = obs.PlayerName,
            Position = obs.Position,
            Recommendation = DecisionRecommendation.Sit,
            RawConfidence = obs.RawConfidence,
            CalibratedConfidence = obs.CalibratedConfidence,
            DecisionValue = obs.DecisionValue,
            DecisionValueMargin = obs.DecisionValueMargin,
            RecommendationMargin = obs.RecommendationMargin,
            ActualDecisionDifferential = obs.ActualDecisionDifferential is null
                ? null
                : -obs.ActualDecisionDifferential.Value,
            WasCorrect = obs.WasCorrect is null ? null : !obs.WasCorrect,
            OpportunityScore = obs.OpportunityScore,
            UsageScore = obs.UsageScore,
            RecentProductionScore = obs.RecentProductionScore
        };

    private static DecisionPolicyScopeMetrics MetricsFromKept(
        string label,
        IReadOnlyList<DecisionPolicyObservation> kept,
        int opportunities,
        int suppressedStarts,
        int suppressedSits,
        int swapped,
        int lowTrust,
        double? suppressedValue)
    {
        var graded = kept.Where(o => o.WasCorrect is not null).ToList();
        var diffs = graded
            .Where(o => o.ActualDecisionDifferential is not null)
            .Select(o => o.ActualDecisionDifferential!.Value)
            .OrderBy(v => v)
            .ToList();
        var dist = BuildConfidenceDistribution(kept);

        return new DecisionPolicyScopeMetrics
        {
            Label = label,
            GradedDecisions = graded.Count,
            Opportunities = opportunities,
            SuppressedStarts = suppressedStarts,
            SuppressedSits = suppressedSits,
            SwappedStarts = swapped,
            LowTrustLabeled = lowTrust,
            AccuracyPercent = graded.Count == 0
                ? null
                : Math.Round(100.0 * graded.Count(g => g.WasCorrect == true) / graded.Count, 1),
            AverageDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Average(), 2),
            TotalDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Sum(), 2),
            WorstDecisionCost = diffs.Count == 0 ? null : Math.Round(diffs.First(), 2),
            BestDecisionValue = diffs.Count == 0 ? null : Math.Round(diffs.Last(), 2),
            SuppressedWouldHaveBeenTotalValue = suppressedValue is null
                ? null
                : Math.Round(suppressedValue.Value, 2),
            ConfidenceDistribution = dist
        };
    }

    public static IReadOnlyDictionary<string, int> BuildConfidenceDistribution(
        IEnumerable<DecisionPolicyObservation> observations)
    {
        var buckets = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["0-40"] = 0,
            ["40-50"] = 0,
            ["50-60"] = 0,
            ["60-70"] = 0,
            ["70-100"] = 0
        };

        foreach (var obs in observations)
        {
            var c = obs.CalibratedConfidence;
            var key = c < 40 ? "0-40"
                : c < 50 ? "40-50"
                : c < 60 ? "50-60"
                : c < 70 ? "60-70"
                : "70-100";
            buckets[key]++;
        }

        return buckets;
    }

    public static DecisionPolicyObservation FromGrade(ReplayDecisionGrade g) =>
        new()
        {
            Season = g.Season,
            Week = g.Week,
            DecisionId = g.DecisionId,
            PlayerId = g.PlayerId,
            PlayerName = g.PlayerName,
            Position = g.Position,
            Recommendation = g.Recommendation,
            RawConfidence = g.Confidence,
            CalibratedConfidence = g.CalibratedConfidence
                ?? FrozenDecisionConfidenceCalibrationV2.Apply(g.Confidence),
            DecisionValue = g.DecisionValue ?? g.ExpectedValue,
            DecisionValueMargin = g.DecisionValueMargin,
            RecommendationMargin = g.RecommendationMargin,
            ActualDecisionDifferential = g.ActualDecisionDifferential,
            WasCorrect = g.WasCorrect,
            OpportunityScore = g.OpportunityScore,
            UsageScore = g.UsageScore,
            RecentProductionScore = g.RecentProductionScore
        };
}
