using Playbook.Application.Abstractions;
using Playbook.Core.Decisions;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Decisions;

/// <summary>
/// Central decision synthesis from structured player knowledge.
///
/// Confidence methodology (v1 — evidence quality, NOT calibrated probability):
/// 1. Start from knowledge confidence (data completeness / assessment quality).
/// 2. Penalize unknowns, conflicts, coverage gaps, and injury uncertainty.
/// 3. Reward signal agreement among known directional evidence.
/// 4. Cap confidence when the lean is provisional (insufficient evidence).
/// Historical replay will later calibrate these values against actual outcomes.
///
/// Expected Value vs Decision Value:
/// - ExpectedValue ≈ projection points (when known).
/// - DecisionValue adjusts EV by health, role/opportunity certainty, coverage, and alternatives.
/// Higher projection alone must not always win a Start recommendation.
/// </summary>
public sealed class DecisionEngine : IDecisionEngine
{
    private readonly IDecisionRecordStore _records;

    public DecisionEngine(IDecisionRecordStore records)
    {
        _records = records;
    }

    public Task<DecisionResult> EvaluatePlayerAsync(
        PlayerKnowledge knowledge,
        DecisionContext context,
        CancellationToken cancellationToken = default)
        => EvaluateDecisionAsync(knowledge, context, alternatives: null, cancellationToken);

    public Task<DecisionResult> EvaluateDecisionAsync(
        PlayerKnowledge knowledge,
        DecisionContext context,
        IReadOnlyList<PlayerKnowledge>? alternatives = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Synthesize(knowledge, context, alternatives));
    }

    public async Task<(DecisionResult Preferred, DecisionResult Other)> ComparePlayersAsync(
        PlayerKnowledge left,
        PlayerKnowledge right,
        DecisionContext context,
        CancellationToken cancellationToken = default)
    {
        var leftResult = await EvaluateDecisionAsync(left, context, [right], cancellationToken).ConfigureAwait(false);
        var rightResult = await EvaluateDecisionAsync(right, context, [left], cancellationToken).ConfigureAwait(false);
        return leftResult.Values.DecisionValue >= rightResult.Values.DecisionValue
            ? (leftResult, rightResult)
            : (rightResult, leftResult);
    }

    public async Task<StartSitDecisionBatch> EvaluateStartSitAsync(
        IReadOnlyList<PlayerKnowledge> rosterKnowledge,
        IReadOnlyList<StartSitCandidate> candidates,
        DecisionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var byId = rosterKnowledge.ToDictionary(k => k.PlayerId);
        var decisions = new List<DecisionResult>();
        var recommendations = new List<StartSitRecommendation>();

        foreach (var group in candidates.GroupBy(c => c.Position).OrderBy(g => PositionOrder(g.Key)))
        {
            if (group.Key is Position.K or Position.DST)
            {
                continue;
            }

            var knowledgeRows = group
                .Select(c => byId.TryGetValue(c.PlayerId, out var k) ? k : null)
                .Where(k => k is not null)
                .Cast<PlayerKnowledge>()
                .ToList();

            if (knowledgeRows.Count == 0)
            {
                continue;
            }

            var evaluated = new List<(PlayerKnowledge Knowledge, DecisionResult Result, double DecisionValue)>();
            foreach (var knowledge in knowledgeRows)
            {
                var alts = knowledgeRows.Where(k => k.PlayerId != knowledge.PlayerId).ToList();
                var result = Synthesize(knowledge, context, alts, forceKind: DecisionKind.StartSit);
                evaluated.Add((knowledge, result, result.Values.DecisionValue));
                decisions.Add(result);
            }

            var ranked = evaluated
                .OrderByDescending(x => x.DecisionValue)
                .ThenByDescending(x => x.Knowledge.ProjectedPoints ?? -1)
                .ThenBy(x => x.Knowledge.PlayerName)
                .ToList();

            var best = ranked[0];
            recommendations.Add(ToStartSit(StartSitAction.Start, best.Result, best.Knowledge));

            foreach (var sit in ranked.Skip(1).Take(2))
            {
                if (sit.Result.IsProvisional && best.Result.IsProvisional)
                {
                    continue;
                }

                if (sit.DecisionValue >= best.DecisionValue - 1.5 && !HasMaterialNegative(sit.Knowledge))
                {
                    continue;
                }

                var sitResult = withRecommendation(sit.Result, DecisionRecommendation.Sit);
                recommendations.Add(ToStartSit(StartSitAction.Sit, sitResult, sit.Knowledge));
            }
        }

        // Starters clearly below a same-position bench option.
        foreach (var starterCandidate in candidates.Where(c => c.IsStarter))
        {
            if (!byId.TryGetValue(starterCandidate.PlayerId, out var starterKnowledge))
            {
                continue;
            }

            var betterBench = candidates
                .Where(c => !c.IsStarter && c.Position == starterCandidate.Position)
                .Select(c => byId.TryGetValue(c.PlayerId, out var k) ? k : null)
                .Where(k => k is not null)
                .Cast<PlayerKnowledge>()
                .Select(k =>
                {
                    var alts = new List<PlayerKnowledge> { starterKnowledge };
                    var result = Synthesize(k, context, alts, DecisionKind.StartSit);
                    return (Knowledge: k, Result: result);
                })
                .Where(x => x.Result.Values.DecisionValue >= Synthesize(starterKnowledge, context, null, DecisionKind.StartSit).Values.DecisionValue + 3)
                .OrderByDescending(x => x.Result.Values.DecisionValue)
                .FirstOrDefault();

            if (betterBench.Knowledge is null)
            {
                continue;
            }

            if (recommendations.Any(r => r.PlayerId == starterCandidate.PlayerId && r.Action == StartSitAction.Sit))
            {
                continue;
            }

            var starterResult = Synthesize(
                starterKnowledge,
                context,
                [betterBench.Knowledge],
                DecisionKind.StartSit);
            starterResult = withRecommendation(starterResult, DecisionRecommendation.Sit);
            var reasons = starterResult.Rationale.ToList();
            reasons.Insert(0, $"{betterBench.Knowledge.PlayerName} currently grades higher for this lineup decision.");
            recommendations.Add(new StartSitRecommendation
            {
                Action = StartSitAction.Sit,
                PlayerId = starterKnowledge.PlayerId,
                PlayerName = starterKnowledge.PlayerName,
                PositionLabel = starterKnowledge.PositionLabel,
                ProjectionSummary = FormatProjection(starterKnowledge),
                Confidence = starterResult.Confidence,
                Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList(),
                InsufficientData = starterResult.IsProvisional
            });
            decisions.Add(starterResult);
        }

        var batch = new StartSitDecisionBatch
        {
            Recommendations = recommendations
                .GroupBy(r => (r.Action, r.PlayerId))
                .Select(g => g.First())
                .OrderBy(r => r.Action == StartSitAction.Start ? 0 : 1)
                .ThenByDescending(r => r.Confidence)
                .Take(10)
                .ToList(),
            Decisions = decisions
        };

        foreach (var decision in batch.Decisions)
        {
            await RecordDecisionAsync(decision, context, cancellationToken).ConfigureAwait(false);
        }

        return batch;

        static DecisionResult withRecommendation(DecisionResult source, DecisionRecommendation recommendation) =>
            new()
            {
                DecisionId = source.DecisionId,
                PlayerId = source.PlayerId,
                PlayerName = source.PlayerName,
                PositionLabel = source.PositionLabel,
                Recommendation = recommendation,
                Confidence = source.Confidence,
                CalibratedConfidence = source.CalibratedConfidence,
                Values = source.Values,
                Facts = source.Facts,
                Inferences = source.Inferences,
                SupportingEvidence = source.SupportingEvidence,
                OpposingEvidence = source.OpposingEvidence,
                Unknowns = source.Unknowns,
                Conflicts = source.Conflicts,
                Rationale = source.Rationale,
                AlternativesConsidered = source.AlternativesConsidered,
                IsProvisional = source.IsProvisional,
                EvidenceStatus = source.EvidenceStatus,
                ProjectionSummary = source.ProjectionSummary,
                GeneratedAt = source.GeneratedAt
            };
    }

    public async Task<DecisionRecord> RecordDecisionAsync(
        DecisionResult result,
        DecisionContext context,
        CancellationToken cancellationToken = default)
    {
        var record = new DecisionRecord
        {
            DecisionId = result.DecisionId,
            PlayerId = result.PlayerId,
            PlayerName = result.PlayerName,
            DecisionType = context.DecisionKind,
            Recommendation = result.Recommendation,
            Confidence = result.Confidence,
            CalibratedConfidence = result.CalibratedConfidence,
            LeagueId = context.LeagueId,
            SelectedRosterId = context.SelectedRosterId,
            LeagueName = context.LeagueName,
            ScoringType = context.ScoringType,
            Season = context.Season,
            Week = context.Week,
            InformationCutoff = context.InformationCutoff,
            CreatedAt = result.GeneratedAt,
            SupportingEvidence = result.SupportingEvidence.Select(s => s.Explanation).ToList(),
            OpposingEvidence = result.OpposingEvidence.Select(s => s.Explanation).ToList(),
            Unknowns = result.Unknowns,
            AlternativesConsidered = result.AlternativesConsidered,
            ExpectedValue = result.Values.ExpectedValue,
            DecisionValue = result.Values.DecisionValue,
            Rationale = result.Rationale,
            ActualOutcome = null,
            EvaluationResult = null
        };

        return await _records.RecordAsync(record, cancellationToken).ConfigureAwait(false);
    }

    private static DecisionResult Synthesize(
        PlayerKnowledge knowledge,
        DecisionContext context,
        IReadOnlyList<PlayerKnowledge>? alternatives,
        DecisionKind? forceKind = null)
    {
        _ = forceKind ?? context.DecisionKind;
        var now = DateTimeOffset.UtcNow;
        var values = AssessValues(knowledge, alternatives);
        var supporting = knowledge.Signals
            .Where(s => s.Direction == SignalDirection.Positive && s.Status != EvidenceStatus.Unknown)
            .OrderByDescending(StrengthRank)
            .ThenByDescending(s => s.Confidence)
            .ToList();

        // Negative evidence plus material uncertainty (coverage/volatility/health) for risk communication.
        var opposing = knowledge.Signals
            .Where(s =>
                s.Direction == SignalDirection.Negative ||
                (s.Direction == SignalDirection.Uncertainty &&
                 s.Type is SignalType.Coverage or SignalType.Volatility or SignalType.Health))
            .GroupBy(s => (s.Type, s.Explanation))
            .Select(g => g.First())
            .OrderByDescending(StrengthRank)
            .ThenByDescending(s => s.Confidence)
            .ToList();

        var unknowns = knowledge.MissingEvidence
            .Concat(knowledge.Signals.Where(s => s.Status == EvidenceStatus.Unknown).Select(s => s.Explanation))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        var conflicts = DetectConflicts(knowledge.Signals);
        var inferences = BuildInferences(knowledge, supporting, opposing);
        var confidence = ComputeDecisionConfidence(knowledge, supporting, opposing, conflicts, unknowns);
        // Experiment 2: informational calibrated confidence. Applied AFTER recommendation derivation
        // and never fed into AssessValues / DeriveRecommendation.
        var calibratedConfidence = FrozenDecisionConfidenceCalibrationV2.Apply(confidence);
        var provisional = knowledge.OverallStatus is EvidenceStatus.Unknown or EvidenceStatus.LowConfidence ||
                          knowledge.KnowledgeConfidence < 40 ||
                          knowledge.ProjectedPoints is null ||
                          unknowns.Count >= 3;

        var recommendation = DeriveRecommendation(
            knowledge,
            values,
            alternatives,
            opposing,
            provisional);

        if (alternatives is { Count: > 0 })
        {
            var bestAlt = alternatives.MaxBy(a => AssessValues(a, null).DecisionValue);
            if (bestAlt is not null &&
                AssessValues(bestAlt, null).DecisionValue > values.DecisionValue + 2.5)
            {
                opposing = opposing
                    .Append(new KnowledgeSignal
                    {
                        Type = SignalType.AlternativeComparison,
                        Value = AssessValues(bestAlt, null).DecisionValue,
                        Direction = SignalDirection.Negative,
                        Strength = SignalStrength.Moderate,
                        Confidence = Math.Min(confidence, bestAlt.KnowledgeConfidence),
                        Status = EvidenceStatus.Known,
                        Source = "DecisionEngine",
                        Explanation = $"{bestAlt.PlayerName} has a higher decision value among alternatives.",
                        ObservedAt = now,
                        Category = "Comparison"
                    })
                    .ToList();

                if (recommendation == DecisionRecommendation.Start)
                {
                    recommendation = DecisionRecommendation.Watch;
                }
            }
        }

        var rationale = BuildRationale(recommendation, knowledge, supporting, opposing, unknowns, conflicts, provisional);

        return new DecisionResult
        {
            DecisionId = Guid.NewGuid(),
            PlayerId = knowledge.PlayerId,
            PlayerName = knowledge.PlayerName,
            PositionLabel = knowledge.PositionLabel,
            Recommendation = recommendation,
            Confidence = confidence,
            CalibratedConfidence = calibratedConfidence,
            Values = values,
            Facts = knowledge.Facts,
            Inferences = inferences,
            SupportingEvidence = supporting.Take(6).ToList(),
            OpposingEvidence = opposing.Take(6).ToList(),
            Unknowns = unknowns,
            Conflicts = conflicts,
            Rationale = rationale,
            AlternativesConsidered = alternatives?.Select(a => a.PlayerId).ToList() ?? [],
            IsProvisional = provisional,
            EvidenceStatus = provisional
                ? (knowledge.OverallStatus == EvidenceStatus.Unknown
                    ? EvidenceStatus.Unknown
                    : EvidenceStatus.LowConfidence)
                : conflicts.Count > 0
                    ? EvidenceStatus.Conflicting
                    : EvidenceStatus.Known,
            ProjectionSummary = FormatProjection(knowledge),
            GeneratedAt = now
        };
    }

    private static ValueAssessment AssessValues(
        PlayerKnowledge knowledge,
        IReadOnlyList<PlayerKnowledge>? alternatives)
    {
        var expected = (double)(knowledge.ProjectedPoints ?? 0m);

        // Decision value: EV weighted, then adjusted by evidence-quality factors.
        var opportunity = (knowledge.OpportunityScore ?? 50) / 100.0 * 10.0;
        var usage = (knowledge.UsageScore ?? 50) / 100.0 * 8.0;
        var intel = knowledge.KnowledgeConfidence / 100.0 * 5.0;
        var health = HealthDecisionAdjustment(knowledge);
        var outlook = OutlookAdjustment(knowledge);
        var coveragePenalty = knowledge.Signals.Any(s => s.Type == SignalType.Coverage) ? -3.0 : 0.0;
        var unknownPenalty = Math.Min(6, knowledge.MissingEvidence.Count * 1.25);

        var decisionValue = (expected * 0.55) + opportunity + usage + intel + health + outlook + coveragePenalty - unknownPenalty;

        // Soft comparative dampening: if alternatives exist with much higher confidence and similar EV,
        // slightly reduce decision value of low-confidence boom projections.
        if (alternatives is { Count: > 0 } &&
            knowledge.KnowledgeConfidence < 40 &&
            knowledge.ProjectedPoints is not null)
        {
            var stableAlt = alternatives.FirstOrDefault(a =>
                a.KnowledgeConfidence >= 55 &&
                a.ProjectedPoints is not null &&
                (double)a.ProjectedPoints.Value + 4 >= expected);
            if (stableAlt is not null)
            {
                decisionValue -= 2.5;
            }
        }

        return new ValueAssessment
        {
            ExpectedValue = expected,
            DecisionValue = decisionValue,
            MethodologyNote =
                "ExpectedValue uses projected points when available. DecisionValue applies opportunity, usage, " +
                "knowledge confidence, health, outlook, coverage/unknown penalties, and soft alternative dampening. " +
                "Not a calibrated probability model."
        };
    }

    private static double HealthDecisionAdjustment(PlayerKnowledge knowledge)
    {
        if (knowledge.Signals.Any(s => s.Type == SignalType.Health && s.Direction == SignalDirection.Negative && s.Strength == SignalStrength.Strong))
        {
            return -12;
        }

        if (knowledge.Signals.Any(s => s.Type == SignalType.Health && s.Direction == SignalDirection.Negative))
        {
            return -5;
        }

        if (knowledge.Signals.Any(s => s.Type == SignalType.Health && s.Direction == SignalDirection.Uncertainty))
        {
            return -1;
        }

        if (knowledge.Signals.Any(s => s.Type == SignalType.Health && s.Direction == SignalDirection.Positive))
        {
            return 2;
        }

        return 0;
    }

    private static double OutlookAdjustment(PlayerKnowledge knowledge)
    {
        var outlook = knowledge.Signals.FirstOrDefault(s => s.Type == SignalType.Outlook);
        if (outlook is null)
        {
            return 0;
        }

        return (outlook.Direction, outlook.Strength) switch
        {
            (SignalDirection.Positive, SignalStrength.Strong) => 4,
            (SignalDirection.Positive, _) => 2,
            (SignalDirection.Negative, SignalStrength.Strong) => -5,
            (SignalDirection.Negative, _) => -3,
            (SignalDirection.Uncertainty, _) => -2,
            _ => 0
        };
    }

    private static int ComputeDecisionConfidence(
        PlayerKnowledge knowledge,
        IReadOnlyList<KnowledgeSignal> supporting,
        IReadOnlyList<KnowledgeSignal> opposing,
        IReadOnlyList<string> conflicts,
        IReadOnlyList<string> unknowns)
    {
        var score = knowledge.KnowledgeConfidence;

        // Agreement bonus
        if (supporting.Count >= 2 && opposing.Count(s => s.Direction == SignalDirection.Negative) == 0)
        {
            score += 6;
        }

        // Conflict / opposition
        score -= conflicts.Count * 8;
        score -= Math.Min(15, opposing.Count(s => s.Direction == SignalDirection.Negative && s.Strength != SignalStrength.Weak) * 4);

        // Unknowns / coverage
        score -= Math.Min(20, unknowns.Count * 4);
        if (knowledge.Signals.Any(s => s.Type == SignalType.Coverage))
        {
            score -= 10;
        }

        // Projection quality
        if (knowledge.ProjectedPoints is null)
        {
            score -= 15;
        }
        else if (knowledge.ProjectionConfidence is int pc && pc < 50)
        {
            score -= 8;
        }

        // Injury certainty: known injury keeps confidence (we are sure of the fact) but decision may still sit.
        // Unconfirmed injury lowers confidence.
        if (knowledge.Signals.Any(s => s.Type == SignalType.Health && s.Status == EvidenceStatus.LowConfidence))
        {
            score -= 6;
        }

        var clamped = Math.Clamp(score, 12, 95);
        if (knowledge.OverallStatus is EvidenceStatus.Unknown or EvidenceStatus.LowConfidence ||
            knowledge.KnowledgeConfidence < 40)
        {
            clamped = Math.Min(clamped, 55);
        }

        return clamped;
    }

    private static DecisionRecommendation DeriveRecommendation(
        PlayerKnowledge knowledge,
        ValueAssessment values,
        IReadOnlyList<PlayerKnowledge>? alternatives,
        IReadOnlyList<KnowledgeSignal> opposing,
        bool provisional)
    {
        var strongInjury = opposing.Any(s =>
            s.Type == SignalType.Health &&
            s.Direction == SignalDirection.Negative &&
            s.Strength == SignalStrength.Strong);

        if (strongInjury && values.DecisionValue < 14)
        {
            return DecisionRecommendation.Sit;
        }

        if (alternatives is { Count: > 0 })
        {
            var bestAltValue = alternatives.Max(a => AssessValues(a, null).DecisionValue);
            if (bestAltValue > values.DecisionValue + 3)
            {
                return provisional ? DecisionRecommendation.Watch : DecisionRecommendation.Sit;
            }

            if (values.DecisionValue >= bestAltValue - 1.0)
            {
                return DecisionRecommendation.Start;
            }
        }

        if (values.DecisionValue >= 12 && !strongInjury)
        {
            return DecisionRecommendation.Start;
        }

        if (values.DecisionValue <= 7 || strongInjury)
        {
            return DecisionRecommendation.Sit;
        }

        return provisional ? DecisionRecommendation.Watch : DecisionRecommendation.NoAction;
    }

    private static IReadOnlyList<DecisionInference> BuildInferences(
        PlayerKnowledge knowledge,
        IReadOnlyList<KnowledgeSignal> supporting,
        IReadOnlyList<KnowledgeSignal> opposing)
    {
        var inferences = new List<DecisionInference>();

        if (supporting.Any(s => s.Type == SignalType.Opportunity && s.Direction == SignalDirection.Positive))
        {
            inferences.Add(new DecisionInference
            {
                Statement = "Strong opportunity supports a higher start lean.",
                Direction = SignalDirection.Positive,
                Confidence = knowledge.KnowledgeConfidence,
                BasedOnFactKeys = ["opportunity.score"]
            });
        }

        if (opposing.Any(s => s.Type == SignalType.Health && s.Direction == SignalDirection.Negative))
        {
            inferences.Add(new DecisionInference
            {
                Statement = "Current health evidence weakens start viability.",
                Direction = SignalDirection.Negative,
                Confidence = 80,
                BasedOnFactKeys = knowledge.Facts.Where(f => f.Key.StartsWith("health", StringComparison.Ordinal)).Select(f => f.Key).ToList()
            });
        }

        if (knowledge.Signals.Any(s => s.Type == SignalType.Coverage))
        {
            inferences.Add(new DecisionInference
            {
                Statement = "Limited intelligence coverage makes any lean provisional.",
                Direction = SignalDirection.Uncertainty,
                Confidence = 70,
                BasedOnFactKeys = ["coverage.intelligence"]
            });
        }

        if (supporting.Any(s => s.Type == SignalType.Projection && s.Direction == SignalDirection.Positive) &&
            knowledge.KnowledgeConfidence >= 50)
        {
            inferences.Add(new DecisionInference
            {
                Statement = "Projection quality is adequate to inform expected value.",
                Direction = SignalDirection.Positive,
                Confidence = knowledge.ProjectionConfidence ?? knowledge.KnowledgeConfidence,
                BasedOnFactKeys = ["projection.points"]
            });
        }

        return inferences;
    }

    private static List<string> DetectConflicts(IReadOnlyList<KnowledgeSignal> signals)
    {
        var conflicts = new List<string>();
        var hasStrongPos = signals.Any(s => s.Direction == SignalDirection.Positive && s.Strength == SignalStrength.Strong && s.Status != EvidenceStatus.Unknown);
        var hasStrongNeg = signals.Any(s => s.Direction == SignalDirection.Negative && s.Strength == SignalStrength.Strong && s.Status != EvidenceStatus.Unknown);
        if (hasStrongPos && hasStrongNeg)
        {
            conflicts.Add("Strong positive and strong negative signals disagree.");
        }

        var highProj = signals.FirstOrDefault(s => s.Type == SignalType.Projection && s.Value is >= 14);
        var lowOpp = signals.FirstOrDefault(s => s.Type == SignalType.Opportunity && s.Direction == SignalDirection.Negative);
        if (highProj is not null && lowOpp is not null)
        {
            conflicts.Add("High projection conflicts with weak opportunity.");
        }

        return conflicts;
    }

    private static IReadOnlyList<string> BuildRationale(
        DecisionRecommendation recommendation,
        PlayerKnowledge knowledge,
        IReadOnlyList<KnowledgeSignal> supporting,
        IReadOnlyList<KnowledgeSignal> opposing,
        IReadOnlyList<string> unknowns,
        IReadOnlyList<string> conflicts,
        bool provisional)
    {
        var reasons = new List<string>();

        if (provisional)
        {
            reasons.Add("Limited supporting intelligence — treat this lean cautiously.");
        }

        if (knowledge.ProjectedPoints is not null)
        {
            reasons.Add(
                $"Projects {knowledge.ProjectedPoints:0.0} pts (floor {knowledge.Floor:0.0} · ceiling {knowledge.Ceiling:0.0}).");
        }
        else
        {
            reasons.Add("Projection unavailable for this player.");
        }

        foreach (var signal in supporting.Take(2))
        {
            reasons.Add(signal.Explanation);
        }

        if (recommendation is DecisionRecommendation.Sit or DecisionRecommendation.Watch)
        {
            foreach (var signal in opposing.Where(s => s.Direction == SignalDirection.Negative).Take(2))
            {
                reasons.Add(signal.Explanation);
            }
        }

        if (conflicts.Count > 0)
        {
            reasons.Add(conflicts[0]);
        }

        if (reasons.Count < 2 && unknowns.Count > 0)
        {
            reasons.Add($"Unknown: {unknowns[0]}");
        }

        return reasons.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
    }

    private static StartSitRecommendation ToStartSit(
        StartSitAction action,
        DecisionResult result,
        PlayerKnowledge knowledge) =>
        new()
        {
            Action = action,
            PlayerId = knowledge.PlayerId,
            PlayerName = knowledge.PlayerName,
            PositionLabel = knowledge.PositionLabel,
            ProjectionSummary = FormatProjection(knowledge),
            Confidence = result.Confidence,
            Reasons = result.Rationale.Take(3).ToList(),
            InsufficientData = result.IsProvisional
        };

    private static bool HasMaterialNegative(PlayerKnowledge knowledge) =>
        knowledge.Signals.Any(s =>
            s.Direction == SignalDirection.Negative &&
            s.Strength != SignalStrength.Weak &&
            s.Type is SignalType.Health or SignalType.Outlook or SignalType.Opportunity);

    private static string? FormatProjection(PlayerKnowledge knowledge) =>
        knowledge.ProjectedPoints is null ? null : $"{knowledge.ProjectedPoints:0.0} pts";

    private static int StrengthRank(KnowledgeSignal s) => s.Strength switch
    {
        SignalStrength.Strong => 3,
        SignalStrength.Moderate => 2,
        _ => 1
    };

    private static int PositionOrder(Position position) => position switch
    {
        Position.QB => 0,
        Position.RB => 1,
        Position.WR => 2,
        Position.TE => 3,
        Position.K => 4,
        Position.DST => 5,
        _ => 9
    };
}
