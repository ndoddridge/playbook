using Playbook.Application.Knowledge;
using Playbook.Core.Decisions;
using Playbook.Core.Knowledge;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Knowledge;

/// <summary>
/// Explicit knowledge-group transforms for Knowledge Impact experiments.
///
/// Transformations (Enhanced only):
/// - Usage: keep OpportunityScore / UsageScore (AssessValues already reads them).
/// - RecentForm: bounded OpportunityScore delta from RecentProductionScore thresholds.
/// - RecentFormThinMargin: same RecentForm deltas, only when ComparativeMargin is thin.
/// - RoleHealth: keep Health signals; bounded OpportunityScore delta from RoleNote heuristics.
/// - DataSufficiencyTrust: KnowledgeConfidence penalty when Limited/Insufficient (no Opportunity restore).
///
/// Baseline: strip Usage/Opportunity/RecentProduction/Role/Health knowledge inputs
/// so AssessValues sees projection + default missing penalties only.
/// </summary>
public sealed class KnowledgeImpactApplicator : IKnowledgeImpactApplicator
{
    private readonly KnowledgeImpactExperimentState _state;

    public KnowledgeImpactApplicator(KnowledgeImpactExperimentState state)
    {
        _state = state;
    }

    public PlayerKnowledge ApplyToPlayerKnowledge(PlayerKnowledge source) =>
        ApplyToPlayerKnowledge(source, comparativeMargin: null);

    public PlayerKnowledge ApplyToPlayerKnowledge(PlayerKnowledge source, double? comparativeMargin) =>
        _state.Mode switch
        {
            KnowledgeMode.Passthrough => source,
            KnowledgeMode.Baseline => ToBaseline(source),
            KnowledgeMode.Enhanced => ToEnhanced(
                source,
                _state.ActiveGroups,
                comparativeMargin,
                _state.ThinMarginMaxPoints,
                _state.DataSufficiencyLimitedPenalty),
            _ => source
        };

    public IReadOnlyList<PlayerKnowledge> ApplyToPlayerKnowledgeBatch(
        IReadOnlyList<PlayerKnowledge> source)
    {
        if (_state.Mode != KnowledgeMode.Enhanced ||
            !_state.ActiveGroups.HasFlag(KnowledgeImpactGroup.RecentFormThinMargin))
        {
            return source.Select(p => ApplyToPlayerKnowledge(p)).ToList();
        }

        var margins = ComputeNearestProjectionMargins(source);
        return source
            .Select(p => ApplyToPlayerKnowledge(
                p, margins.TryGetValue(p.PlayerId, out var m) ? m : null))
            .ToList();
    }

    public Prediction ApplyToQuickPickPrediction(Prediction source, PredictionContext? knowledgeContext)
    {
        if (_state.Mode is KnowledgeMode.Passthrough or KnowledgeMode.Baseline ||
            _state.ActiveGroups == KnowledgeImpactGroup.None ||
            knowledgeContext is null)
        {
            return source;
        }

        var delta = 0m;
        var notes = new List<string>();

        if (_state.ActiveGroups.HasFlag(KnowledgeImpactGroup.Usage))
        {
            var usage = knowledgeContext.Knowledge.Evidence
                .FirstOrDefault(e => e.Aspect == KnowledgeAspect.Usage && !e.IsUnavailableMarker);
            if (usage?.Direction == SignalDirection.Positive)
            {
                delta += 0.8m;
                notes.Add("Knowledge Usage+: +0.8 opportunity");
            }
            else if (usage?.Direction == SignalDirection.Negative)
            {
                delta -= 0.8m;
                notes.Add("Knowledge Usage-: -0.8 opportunity");
            }
        }

        var applyRecentForm =
            _state.ActiveGroups.HasFlag(KnowledgeImpactGroup.RecentForm) ||
            (_state.ActiveGroups.HasFlag(KnowledgeImpactGroup.RecentFormThinMargin) &&
             IsThinMargin(knowledgeContext.ComparativeMargin, _state.ThinMarginMaxPoints));

        if (_state.ActiveGroups.HasFlag(KnowledgeImpactGroup.RecentFormThinMargin) &&
            !IsThinMargin(knowledgeContext.ComparativeMargin, _state.ThinMarginMaxPoints))
        {
            notes.Add(
                $"RecentFormThinMargin gate closed: margin=" +
                $"{knowledgeContext.ComparativeMargin?.ToString("0.###") ?? "n/a"} " +
                $"(max={_state.ThinMarginMaxPoints:0.###})");
        }

        if (applyRecentForm)
        {
            var form = knowledgeContext.Knowledge.Evidence
                .FirstOrDefault(e => e.Aspect == KnowledgeAspect.RecentProduction && !e.IsUnavailableMarker);
            if (form?.Value is >= FrozenKnowledgeImpactExperimentV1.RecentFormHighThreshold)
            {
                delta += 0.6m;
                notes.Add("Knowledge RecentForm+: +0.6 opportunity");
            }
            else if (form?.Value is <= FrozenKnowledgeImpactExperimentV1.RecentFormLowThreshold
                     and > 0)
            {
                delta -= 0.6m;
                notes.Add("Knowledge RecentForm-: -0.6 opportunity");
            }
        }

        if (_state.ActiveGroups.HasFlag(KnowledgeImpactGroup.RoleHealth))
        {
            var injury = knowledgeContext.Knowledge.Evidence
                .FirstOrDefault(e =>
                    e.Aspect is KnowledgeAspect.InjuryStatus or KnowledgeAspect.Health &&
                    e.Direction == SignalDirection.Negative &&
                    !e.IsUnavailableMarker);
            if (injury is not null)
            {
                delta -= injury.Strength == SignalStrength.Strong ? 1.5m : 0.7m;
                notes.Add("Knowledge Health-: opportunity penalty");
            }
        }

        var confidence = source.Confidence;
        if (_state.ActiveGroups.HasFlag(KnowledgeImpactGroup.DataSufficiencyTrust))
        {
            var sufficiency = ExtractDataSufficiency(knowledgeContext.Knowledge.Facts);
            var penalty = PenaltyFor(sufficiency, _state.DataSufficiencyLimitedPenalty);
            if (penalty > 0)
            {
                var before = confidence;
                confidence = Math.Clamp(confidence - penalty, 12, 95);
                notes.Add(
                    $"DataSufficiencyTrust: {sufficiency} Confidence {before}→{confidence} (−{penalty})");
            }
        }

        if (delta == 0m && confidence == source.Confidence)
        {
            return source;
        }

        var adjusted = Math.Clamp(source.OpportunityScore + delta, 0m, 100m);
        var calc = source.CalculationNotes.ToList();
        calc.AddRange(notes);
        if (delta != 0m)
        {
            calc.Add($"KnowledgeImpact { _state.ActiveGroups }: OpportunityScore {source.OpportunityScore:0.00} → {adjusted:0.00}");
        }

        return new Prediction
        {
            Id = source.Id,
            Event = source.Event,
            PlayerId = source.PlayerId,
            PlayerName = source.PlayerName,
            TeamName = source.TeamName,
            Market = source.Market,
            Line = source.Line,
            PlaybookProjection = source.PlaybookProjection,
            Probability = source.Probability,
            Edge = source.Edge,
            Confidence = confidence,
            Direction = source.Direction,
            Reasoning = source.Reasoning,
            SupportingIntelligence = source.SupportingIntelligence,
            SignalContributions = source.SignalContributions,
            CalculationNotes = calc,
            Source = source.Source,
            LineFreshness = source.LineFreshness,
            LastUpdated = source.LastUpdated,
            LineUpdatedAt = source.LineUpdatedAt,
            Bookmaker = source.Bookmaker,
            EngineVersion = source.EngineVersion,
            OpportunityScore = adjusted
        };
    }

    /// <summary>Pure Baseline transform for tests / offline use.</summary>
    public static PlayerKnowledge ToBaseline(PlayerKnowledge source)
    {
        var missing = source.MissingEvidence.ToList();
        EnsureMissing(missing, "Opportunity");
        EnsureMissing(missing, "Usage");
        EnsureMissing(missing, "Recent production");
        EnsureMissing(missing, "Role");
        EnsureMissing(missing, "Health knowledge");

        var signals = source.Signals
            .Where(s => s.Type is not (
                SignalType.Opportunity or
                SignalType.Usage or
                SignalType.RecentProduction or
                SignalType.Role or
                SignalType.Health or
                SignalType.Outlook))
            .ToList();

        var facts = source.Facts
            .Where(f =>
                !f.Key.StartsWith("opportunity", StringComparison.OrdinalIgnoreCase) &&
                !f.Key.StartsWith("usage", StringComparison.OrdinalIgnoreCase) &&
                !f.Key.StartsWith("production", StringComparison.OrdinalIgnoreCase) &&
                !f.Key.StartsWith("role", StringComparison.OrdinalIgnoreCase) &&
                !f.Key.StartsWith("health", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Clone(
            source,
            facts,
            signals,
            opportunity: null,
            usage: null,
            healthLabel: null,
            missing,
            knowledgeConfidence: Math.Clamp(source.KnowledgeConfidence - 15, 15, 85));
    }

    /// <summary>Pure Enhanced transform for tests / offline use.</summary>
    public static PlayerKnowledge ToEnhanced(PlayerKnowledge source, KnowledgeImpactGroup groups) =>
        ToEnhanced(
            source,
            groups,
            comparativeMargin: null,
            thinMarginMax: FrozenRecentFormThinMarginExperimentV1.ThinMarginMaxPoints,
            limitedPenalty: FrozenDataSufficiencyTrustExperimentV1.SelectedLimitedPenalty);

    /// <summary>Enhanced transform with optional comparative margin for thin-margin gating.</summary>
    public static PlayerKnowledge ToEnhanced(
        PlayerKnowledge source,
        KnowledgeImpactGroup groups,
        double? comparativeMargin,
        double thinMarginMax) =>
        ToEnhanced(
            source,
            groups,
            comparativeMargin,
            thinMarginMax,
            FrozenDataSufficiencyTrustExperimentV1.SelectedLimitedPenalty);

    /// <summary>Enhanced transform with margin + data-sufficiency trust parameters.</summary>
    public static PlayerKnowledge ToEnhanced(
        PlayerKnowledge source,
        KnowledgeImpactGroup groups,
        double? comparativeMargin,
        double thinMarginMax,
        int limitedPenalty)
    {
        var working = ToBaseline(source);
        var opportunity = working.OpportunityScore;
        var usage = working.UsageScore;
        var signals = working.Signals.ToList();
        var facts = working.Facts.ToList();
        var missing = working.MissingEvidence.ToList();
        var healthLabel = working.HealthLabel;
        var confidence = working.KnowledgeConfidence;
        var transformNotes = new List<string>();

        if (groups.HasFlag(KnowledgeImpactGroup.Usage))
        {
            opportunity = source.OpportunityScore;
            usage = source.UsageScore;
            RemoveMissing(missing, "Opportunity");
            RemoveMissing(missing, "Usage");
            foreach (var s in source.Signals.Where(s => s.Type is SignalType.Opportunity or SignalType.Usage))
            {
                signals.Add(s);
            }

            foreach (var f in source.Facts.Where(f =>
                         f.Key.StartsWith("opportunity", StringComparison.OrdinalIgnoreCase) ||
                         f.Key.StartsWith("usage", StringComparison.OrdinalIgnoreCase)))
            {
                facts.Add(f);
            }

            transformNotes.Add(
                $"Usage group: Opportunity={opportunity?.ToString() ?? "null"} Usage={usage?.ToString() ?? "null"}");
            confidence = Math.Clamp(confidence + 5, 15, 90);
        }

        var recentFormActive = groups.HasFlag(KnowledgeImpactGroup.RecentForm);
        var thinMarginActive = groups.HasFlag(KnowledgeImpactGroup.RecentFormThinMargin);
        if (recentFormActive || thinMarginActive)
        {
            RemoveMissing(missing, "Recent production");
            foreach (var s in source.Signals.Where(s => s.Type == SignalType.RecentProduction))
            {
                signals.Add(s);
            }

            foreach (var f in source.Facts.Where(f =>
                         f.Key.StartsWith("production", StringComparison.OrdinalIgnoreCase)))
            {
                facts.Add(f);
            }

            var gateOpen = recentFormActive || IsThinMargin(comparativeMargin, thinMarginMax);
            if (thinMarginActive && !gateOpen)
            {
                transformNotes.Add(
                    $"RecentFormThinMargin gate closed: margin=" +
                    $"{comparativeMargin?.ToString("0.###") ?? "n/a"} (max={thinMarginMax:0.###})");
            }

            var prod = ExtractRecentProduction(source);
            if (prod is int score)
            {
                if (!gateOpen)
                {
                    transformNotes.Add($"RecentFormThinMargin~: prod={score} no opportunity delta (gate closed)");
                }
                else
                {
                    var before = opportunity ?? 50;
                    var label = thinMarginActive ? "RecentFormThinMargin" : "RecentForm";
                    if (score >= FrozenKnowledgeImpactExperimentV1.RecentFormHighThreshold)
                    {
                        opportunity = Math.Clamp(
                            before + FrozenKnowledgeImpactExperimentV1.RecentFormOpportunityDelta,
                            0,
                            100);
                        transformNotes.Add(
                            $"{label}+: prod={score} margin={comparativeMargin?.ToString("0.###") ?? "n/a"} " +
                            $"Opportunity {before}→{opportunity} " +
                            $"(+{FrozenKnowledgeImpactExperimentV1.RecentFormOpportunityDelta})");
                    }
                    else if (score <= FrozenKnowledgeImpactExperimentV1.RecentFormLowThreshold)
                    {
                        opportunity = Math.Clamp(
                            before - FrozenKnowledgeImpactExperimentV1.RecentFormOpportunityDelta,
                            0,
                            100);
                        transformNotes.Add(
                            $"{label}-: prod={score} margin={comparativeMargin?.ToString("0.###") ?? "n/a"} " +
                            $"Opportunity {before}→{opportunity} " +
                            $"(-{FrozenKnowledgeImpactExperimentV1.RecentFormOpportunityDelta})");
                    }
                    else
                    {
                        transformNotes.Add($"{label}~: prod={score} no opportunity delta");
                    }
                }
            }
        }

        if (groups.HasFlag(KnowledgeImpactGroup.RoleHealth))
        {
            RemoveMissing(missing, "Role");
            RemoveMissing(missing, "Health knowledge");
            healthLabel = source.HealthLabel;
            foreach (var s in source.Signals.Where(s => s.Type is SignalType.Role or SignalType.Health))
            {
                signals.Add(s);
            }

            foreach (var f in source.Facts.Where(f =>
                         f.Key.StartsWith("role", StringComparison.OrdinalIgnoreCase) ||
                         f.Key.StartsWith("health", StringComparison.OrdinalIgnoreCase)))
            {
                facts.Add(f);
            }

            var roleDelta = RoleOpportunityDelta(source);
            if (roleDelta != 0)
            {
                var before = opportunity ?? 50;
                opportunity = Math.Clamp(before + roleDelta, 0, 100);
                transformNotes.Add($"RoleHealth roleΔ={roleDelta}: Opportunity {before}→{opportunity}");
            }
            else
            {
                transformNotes.Add("RoleHealth: health signals restored; no role opportunity delta");
            }
        }

        _ = groups.HasFlag(KnowledgeImpactGroup.Matchup);

        if (groups.HasFlag(KnowledgeImpactGroup.DataSufficiencyTrust))
        {
            // Trust-only: do not restore Usage / RecentForm / RoleHealth Opportunity scores.
            var sufficiency = ExtractDataSufficiency(source.Facts) ?? ExtractDataSufficiency(facts);
            var penalty = PenaltyFor(sufficiency, limitedPenalty);
            if (penalty > 0)
            {
                var before = confidence;
                confidence = Math.Clamp(confidence - penalty, 12, 95);
                transformNotes.Add(
                    $"DataSufficiencyTrust: {sufficiency} KnowledgeConfidence {before}→{confidence} (−{penalty})");
            }
            else
            {
                transformNotes.Add(
                    $"DataSufficiencyTrust: {sufficiency?.ToString() ?? "unknown"} no confidence penalty");
            }
        }

        var sourceId = groups.HasFlag(KnowledgeImpactGroup.DataSufficiencyTrust)
            ? FrozenDataSufficiencyTrustExperimentV1.ExperimentId
            : thinMarginActive
                ? FrozenRecentFormThinMarginExperimentV1.ExperimentId
                : FrozenKnowledgeImpactExperimentV1.ExperimentId;

        foreach (var note in transformNotes)
        {
            facts.Add(new KnowledgeFact
            {
                Key = "knowledge_impact.transform",
                Statement = note,
                Source = sourceId,
                ObservedAt = source.InformationCutoff ?? source.GeneratedAt,
                Status = EvidenceStatus.Known
            });
        }

        return Clone(source, facts, signals, opportunity, usage, healthLabel, missing, confidence);
    }

    public static DataSufficiency? ExtractDataSufficiency(IEnumerable<KnowledgeFact> facts)
    {
        var fact = facts.FirstOrDefault(f =>
            f.Key.Equals("projection.data_sufficiency", StringComparison.OrdinalIgnoreCase));
        if (fact is null)
        {
            return null;
        }

        if (fact.Statement.Contains("Insufficient", StringComparison.OrdinalIgnoreCase))
        {
            return DataSufficiency.Insufficient;
        }

        if (fact.Statement.Contains("Limited", StringComparison.OrdinalIgnoreCase))
        {
            return DataSufficiency.Limited;
        }

        if (fact.Statement.Contains("Sufficient", StringComparison.OrdinalIgnoreCase))
        {
            return DataSufficiency.Sufficient;
        }

        return null;
    }

    public static int PenaltyFor(DataSufficiency? sufficiency, int limitedPenalty) =>
        sufficiency switch
        {
            DataSufficiency.Limited => limitedPenalty,
            DataSufficiency.Insufficient => limitedPenalty + FrozenDataSufficiencyTrustExperimentV1.InsufficientExtraPenalty,
            _ => 0
        };

    public static bool IsThinMargin(double? comparativeMargin, double thinMarginMax) =>
        comparativeMargin is double m && m < thinMarginMax;

    /// <summary>
    /// Nearest same-position projected-points gap. Null when fewer than two projected peers.
    /// </summary>
    public static IReadOnlyDictionary<Guid, double?> ComputeNearestProjectionMargins(
        IReadOnlyList<PlayerKnowledge> source)
    {
        var result = new Dictionary<Guid, double?>();
        foreach (var group in source.GroupBy(p => p.PositionLabel ?? "?"))
        {
            var withProj = group.Where(p => p.ProjectedPoints is not null).ToList();
            foreach (var player in group)
            {
                if (player.ProjectedPoints is null || withProj.Count < 2)
                {
                    result[player.PlayerId] = null;
                    continue;
                }

                var proj = (double)player.ProjectedPoints.Value;
                var nearest = withProj
                    .Where(o => o.PlayerId != player.PlayerId)
                    .Min(o => Math.Abs((double)o.ProjectedPoints!.Value - proj));
                result[player.PlayerId] = nearest;
            }
        }

        return result;
    }

    private static int? ExtractRecentProduction(PlayerKnowledge source)
    {
        var signal = source.Signals.FirstOrDefault(s => s.Type == SignalType.RecentProduction && s.Value is not null);
        if (signal?.Value is double v)
        {
            return (int)Math.Round(v);
        }

        return null;
    }

    private static int RoleOpportunityDelta(PlayerKnowledge source)
    {
        var role = source.Signals.FirstOrDefault(s => s.Type == SignalType.Role)?.Explanation
                   ?? source.Facts.FirstOrDefault(f => f.Key.StartsWith("role", StringComparison.OrdinalIgnoreCase))
                       ?.Statement;
        if (string.IsNullOrWhiteSpace(role))
        {
            return 0;
        }

        if (ContainsAny(role, "starter", "featured", "wr1", "rb1", "te1", "qb1", "lead back", "primary"))
        {
            return FrozenKnowledgeImpactExperimentV1.RoleOpportunityDelta;
        }

        if (ContainsAny(role, "backup", "limited", "depth", "committee", "inactive", "third string"))
        {
            return -FrozenKnowledgeImpactExperimentV1.RoleOpportunityDelta;
        }

        return 0;
    }

    private static bool ContainsAny(string text, params string[] tokens) =>
        tokens.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static void EnsureMissing(List<string> missing, string label)
    {
        if (!missing.Any(m => m.Equals(label, StringComparison.OrdinalIgnoreCase)))
        {
            missing.Add(label);
        }
    }

    private static void RemoveMissing(List<string> missing, string label) =>
        missing.RemoveAll(m => m.Equals(label, StringComparison.OrdinalIgnoreCase));

    private static PlayerKnowledge Clone(
        PlayerKnowledge source,
        IReadOnlyList<KnowledgeFact> facts,
        IReadOnlyList<KnowledgeSignal> signals,
        int? opportunity,
        int? usage,
        string? healthLabel,
        IReadOnlyList<string> missing,
        int knowledgeConfidence) =>
        new()
        {
            PlayerId = source.PlayerId,
            PlayerName = source.PlayerName,
            PositionLabel = source.PositionLabel,
            Facts = facts,
            Signals = signals,
            OverallStatus = source.OverallStatus,
            KnowledgeConfidence = knowledgeConfidence,
            MissingEvidence = missing,
            GeneratedAt = source.GeneratedAt,
            InformationCutoff = source.InformationCutoff,
            ProjectedPoints = source.ProjectedPoints,
            Floor = source.Floor,
            Ceiling = source.Ceiling,
            ProjectionConfidence = source.ProjectionConfidence,
            OpportunityScore = opportunity,
            UsageScore = usage,
            HealthLabel = healthLabel
        };
}
