using Playbook.Application.Knowledge;
using Playbook.Core.Decisions;
using Playbook.Core.Knowledge;
using Playbook.Core.Predictions;

namespace Playbook.Infrastructure.Knowledge;

/// <summary>
/// Explicit knowledge-group transforms for Knowledge Impact Experiment V1.
///
/// Transformations (Enhanced only):
/// - Usage: keep OpportunityScore / UsageScore (AssessValues already reads them).
/// - RecentForm: bounded OpportunityScore delta from RecentProductionScore thresholds.
/// - RoleHealth: keep Health signals; bounded OpportunityScore delta from RoleNote heuristics.
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
        _state.Mode switch
        {
            KnowledgeMode.Passthrough => source,
            KnowledgeMode.Baseline => ToBaseline(source),
            KnowledgeMode.Enhanced => ToEnhanced(source, _state.ActiveGroups),
            _ => source
        };

    public IReadOnlyList<PlayerKnowledge> ApplyToPlayerKnowledgeBatch(
        IReadOnlyList<PlayerKnowledge> source) =>
        source.Select(ApplyToPlayerKnowledge).ToList();

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

        if (_state.ActiveGroups.HasFlag(KnowledgeImpactGroup.RecentForm))
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

        if (delta == 0m)
        {
            return source;
        }

        var adjusted = Math.Clamp(source.OpportunityScore + delta, 0m, 100m);
        var calc = source.CalculationNotes.ToList();
        calc.AddRange(notes);
        calc.Add($"KnowledgeImpact { _state.ActiveGroups }: OpportunityScore {source.OpportunityScore:0.00} → {adjusted:0.00}");

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
            Confidence = source.Confidence,
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

        // Keep projection / floor / ceiling / coverage / news / volatility / matchup.
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
    public static PlayerKnowledge ToEnhanced(PlayerKnowledge source, KnowledgeImpactGroup groups)
    {
        // Start from baseline (no groups), then re-apply selected groups explicitly.
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

        if (groups.HasFlag(KnowledgeImpactGroup.RecentForm))
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

            var prod = ExtractRecentProduction(source);
            if (prod is int score)
            {
                var before = opportunity ?? 50;
                if (score >= FrozenKnowledgeImpactExperimentV1.RecentFormHighThreshold)
                {
                    opportunity = Math.Clamp(
                        before + FrozenKnowledgeImpactExperimentV1.RecentFormOpportunityDelta,
                        0,
                        100);
                    transformNotes.Add(
                        $"RecentForm+: prod={score} Opportunity {before}→{opportunity} " +
                        $"(+{FrozenKnowledgeImpactExperimentV1.RecentFormOpportunityDelta})");
                }
                else if (score <= FrozenKnowledgeImpactExperimentV1.RecentFormLowThreshold)
                {
                    opportunity = Math.Clamp(
                        before - FrozenKnowledgeImpactExperimentV1.RecentFormOpportunityDelta,
                        0,
                        100);
                    transformNotes.Add(
                        $"RecentForm-: prod={score} Opportunity {before}→{opportunity} " +
                        $"(-{FrozenKnowledgeImpactExperimentV1.RecentFormOpportunityDelta})");
                }
                else
                {
                    transformNotes.Add($"RecentForm~: prod={score} no opportunity delta");
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

        // Matchup intentionally ignored — insufficient historical coverage.
        _ = groups.HasFlag(KnowledgeImpactGroup.Matchup);

        foreach (var note in transformNotes)
        {
            facts.Add(new KnowledgeFact
            {
                Key = "knowledge_impact.transform",
                Statement = note,
                Source = FrozenKnowledgeImpactExperimentV1.ExperimentId,
                ObservedAt = source.InformationCutoff ?? source.GeneratedAt,
                Status = EvidenceStatus.Known
            });
        }

        return Clone(source, facts, signals, opportunity, usage, healthLabel, missing, confidence);
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
