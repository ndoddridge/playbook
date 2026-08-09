using Playbook.Application.Replay;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Calibration;

/// <summary>
/// Applies the confidence-aware decision policy as a separate post-ranking layer.
/// Does not modify projection or confidence calibration formulas.
/// </summary>
public sealed class ConfidenceAwareDecisionPolicyApplicator : IConfidenceAwareDecisionPolicy
{
    private readonly ConfidenceAwareDecisionPolicyState _state;

    public ConfidenceAwareDecisionPolicyApplicator(ConfidenceAwareDecisionPolicyState state)
    {
        _state = state;
    }

    public ConfidenceAwareDecisionPolicyDefinition ActiveDefinition =>
        ConfidenceAwareDecisionPolicyDefinition.FromFrozen();

    public ConfidenceAwareDecisionPolicyApplicationResult Apply(
        IReadOnlyList<StartSitRecommendation> rankedRecommendations,
        ConfidenceAwareDecisionPolicyDefinition? overrideDefinition = null)
    {
        if (_state.Mode == ConfidenceAwareDecisionPolicyMode.Off && overrideDefinition is null)
        {
            // Control: no trust labels, no suppression.
            return new ConfidenceAwareDecisionPolicyApplicationResult(
                rankedRecommendations,
                AffectedCount: 0,
                SuppressedCount: 0,
                SwappedCount: 0,
                LowTrustLabeledCount: 0,
                Notes: ["policy-off"]);
        }

        var definition = overrideDefinition ?? ActiveDefinition;
        return ApplyDefinition(rankedRecommendations, definition);
    }

    /// <summary>Pure application used by tests and offline simulation parity checks.</summary>
    public static ConfidenceAwareDecisionPolicyApplicationResult ApplyDefinition(
        IReadOnlyList<StartSitRecommendation> rankedRecommendations,
        ConfidenceAwareDecisionPolicyDefinition definition)
    {
        var notes = new List<string>();
        var kept = new List<StartSitRecommendation>();
        var suppressed = 0;
        var swapped = 0;
        var lowTrust = 0;
        var affected = 0;

        // Group by position so SwapStart can promote the next same-position alternative when present.
        var byPosition = rankedRecommendations
            .GroupBy(r => r.PositionLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in byPosition)
        {
            var rows = group.ToList();
            var start = rows.FirstOrDefault(r => r.Action == StartSitAction.Start);
            var sits = rows.Where(r => r.Action == StartSitAction.Sit).ToList();

            if (start is not null && ShouldActOn(start, definition))
            {
                affected++;
                if (definition.Kind == DecisionPolicyKinds.SwapStart && sits.Count > 0)
                {
                    var alt = sits[0];
                    kept.Add(WithTrust(
                        CloneWithAction(alt, StartSitAction.Start),
                        definition,
                        forceLow: true,
                        reason: $"Swapped Start: prior pick had calibrated confidence {start.CalibratedConfidence}% with thin edge."));
                    kept.Add(WithTrust(
                        CloneWithAction(start, StartSitAction.Sit),
                        definition,
                        forceLow: true,
                        reason: $"Demoted Start: calibrated confidence {start.CalibratedConfidence}% contradicted thin DecisionValue edge."));
                    swapped++;
                    notes.Add($"swap-start:{start.PlayerName}->{alt.PlayerName}");
                    foreach (var sit in sits.Skip(1))
                    {
                        kept.Add(LabelOnly(sit, definition, ref lowTrust));
                    }

                    continue;
                }

                // SuppressStart / SuppressStartAndSit / SwapStart without alternative → suppress Start.
                suppressed++;
                notes.Add($"suppress-start:{start.PlayerName}");
                foreach (var sit in sits)
                {
                    if (definition.Kind == DecisionPolicyKinds.SuppressStartAndSit && ShouldActOn(sit, definition))
                    {
                        suppressed++;
                        affected++;
                        notes.Add($"suppress-sit:{sit.PlayerName}");
                        continue;
                    }

                    kept.Add(LabelOnly(sit, definition, ref lowTrust));
                }

                continue;
            }

            if (start is not null)
            {
                kept.Add(LabelOnly(start, definition, ref lowTrust));
            }

            foreach (var sit in sits)
            {
                if (definition.Kind == DecisionPolicyKinds.SuppressStartAndSit && ShouldActOn(sit, definition))
                {
                    suppressed++;
                    affected++;
                    notes.Add($"suppress-sit:{sit.PlayerName}");
                    continue;
                }

                kept.Add(LabelOnly(sit, definition, ref lowTrust));
            }
        }

        return new ConfidenceAwareDecisionPolicyApplicationResult(
            kept,
            AffectedCount: affected,
            SuppressedCount: suppressed,
            SwappedCount: swapped,
            LowTrustLabeledCount: lowTrust,
            Notes: notes);
    }

    private static bool ShouldActOn(
        StartSitRecommendation rec,
        ConfidenceAwareDecisionPolicyDefinition definition)
    {
        var cal = rec.CalibratedConfidence ?? FrozenDecisionConfidenceCalibrationV2.Apply(rec.Confidence);
        if (cal > definition.Threshold)
        {
            return false;
        }

        var margin = rec.DecisionValueMargin;
        if (margin is null)
        {
            // Without a known edge, treat as marginal (weak evidence case).
            return true;
        }

        return margin.Value < definition.Margin;
    }

    private static StartSitRecommendation LabelOnly(
        StartSitRecommendation rec,
        ConfidenceAwareDecisionPolicyDefinition definition,
        ref int lowTrust)
    {
        var labeled = WithTrust(rec, definition, forceLow: false, reason: null);
        if (labeled.TrustLabel == DecisionTrustLabel.LowTrust)
        {
            lowTrust++;
        }

        return labeled;
    }

    private static StartSitRecommendation WithTrust(
        StartSitRecommendation rec,
        ConfidenceAwareDecisionPolicyDefinition definition,
        bool forceLow,
        string? reason)
    {
        var cal = rec.CalibratedConfidence ?? FrozenDecisionConfidenceCalibrationV2.Apply(rec.Confidence);
        var label = forceLow
            ? DecisionTrustLabel.LowTrust
            : cal >= definition.HighTrustMin
                ? DecisionTrustLabel.HighTrust
                : DecisionTrustLabel.LowTrust;

        var trustReason = reason ??
            (label == DecisionTrustLabel.HighTrust
                ? $"HIGH TRUST — calibrated confidence {cal}%."
                : $"LOW TRUST — calibrated confidence {cal}%.");

        return new StartSitRecommendation
        {
            Action = rec.Action,
            PlayerId = rec.PlayerId,
            PlayerName = rec.PlayerName,
            PositionLabel = rec.PositionLabel,
            ProjectionSummary = rec.ProjectionSummary,
            Confidence = rec.Confidence,
            CalibratedConfidence = cal,
            DecisionValue = rec.DecisionValue,
            DecisionValueMargin = rec.DecisionValueMargin,
            TrustLabel = label,
            TrustReason = trustReason,
            Reasons = MergeReason(rec.Reasons, trustReason),
            InsufficientData = rec.InsufficientData || label == DecisionTrustLabel.LowTrust
        };
    }

    private static StartSitRecommendation CloneWithAction(
        StartSitRecommendation rec,
        StartSitAction action) =>
        new()
        {
            Action = action,
            PlayerId = rec.PlayerId,
            PlayerName = rec.PlayerName,
            PositionLabel = rec.PositionLabel,
            ProjectionSummary = rec.ProjectionSummary,
            Confidence = rec.Confidence,
            CalibratedConfidence = rec.CalibratedConfidence,
            DecisionValue = rec.DecisionValue,
            DecisionValueMargin = rec.DecisionValueMargin,
            TrustLabel = rec.TrustLabel,
            TrustReason = rec.TrustReason,
            Reasons = rec.Reasons,
            InsufficientData = rec.InsufficientData
        };

    private static IReadOnlyList<string> MergeReason(IReadOnlyList<string> reasons, string trustReason)
    {
        var list = reasons.ToList();
        if (!list.Any(r => r.Contains("TRUST", StringComparison.OrdinalIgnoreCase)))
        {
            list.Insert(0, trustReason);
        }

        return list.Take(4).ToList();
    }
}
