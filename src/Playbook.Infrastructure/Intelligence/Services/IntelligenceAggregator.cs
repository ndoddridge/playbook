using Microsoft.Extensions.Options;
using Playbook.Application.Intelligence;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Intelligence.Services;

/// <summary>
/// Deterministic evidence aggregation: group → dedupe → weighted scores → profile.
/// </summary>
public sealed class IntelligenceAggregator : IIntelligenceAggregator
{
    private readonly IntelligenceScoringOptions _scoring;

    public IntelligenceAggregator(IOptions<IntelligenceScoringOptions> scoring)
    {
        _scoring = scoring.Value;
    }

    public IReadOnlyList<PlayerIntelligenceProfile> Aggregate(IReadOnlyList<IntelligenceFact> facts)
    {
        var baseline = _scoring.BaselineScore;
        var ruleMap = _scoring.Rules
            .Where(r => !string.IsNullOrWhiteSpace(r.RuleId))
            .GroupBy(r => r.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return facts
            .Where(f => f.RelatedPlayerId is not null)
            .GroupBy(f => f.RelatedPlayerId!.Value)
            .OrderBy(g => g.Key)
            .Select(group => BuildProfile(group.Key, group.ToList(), baseline, ruleMap))
            .OrderByDescending(p => Math.Abs(p.OpportunityScore - baseline) + Math.Abs(p.HealthScore - baseline) + p.NewsMomentum)
            .ThenByDescending(p => p.OverallConfidence)
            .ThenBy(p => p.PlayerId)
            .ToList();
    }

    private PlayerIntelligenceProfile BuildProfile(
        Guid playerId,
        IReadOnlyList<IntelligenceFact> rawFacts,
        int baseline,
        IReadOnlyDictionary<string, ScoreRuleOptions> ruleMap)
    {
        var supporting = DedupeFacts(rawFacts);
        double health = baseline;
        double opportunity = baseline;
        double usage = baseline;
        double risk = 0;
        double momentum = 0;
        double confidenceAcc = 0;
        double weightAcc = 0;

        foreach (var fact in supporting.OrderBy(f => f.Id))
        {
            var weight = ImportanceWeight(fact.Importance) * (fact.Confidence / 100.0);
            weightAcc += weight;
            confidenceAcc += fact.Confidence * weight;
            momentum += weight * 8;

            var ruleId = ExtractRuleId(fact);
            if (ruleId is not null && ruleMap.TryGetValue(ruleId, out var rule))
            {
                health += rule.HealthDelta * weight;
                opportunity += rule.OpportunityDelta * weight;
                usage += rule.UsageDelta * weight;
                risk += rule.RiskDelta * weight;
                momentum += rule.MomentumDelta * weight;
            }
            else
            {
                ApplyCategoryFallback(fact.Category, weight, ref health, ref opportunity, ref usage, ref risk);
            }
        }

        var healthScore = Clamp(health);
        var opportunityScore = Clamp(opportunity);
        var usageScore = Clamp(usage);
        var overallRisk = Clamp(risk);
        var newsMomentum = Clamp(momentum);
        var overallConfidence = weightAcc <= 0
            ? baseline
            : Clamp(confidenceAcc / weightAcc);

        var trend = InferTrend(healthScore, opportunityScore, usageScore, overallRisk, baseline);
        var signal = InferSignal(healthScore, opportunityScore, usageScore, overallRisk, baseline);

        return new PlayerIntelligenceProfile
        {
            PlayerId = playerId,
            OverallConfidence = (int)Math.Round(overallConfidence),
            OverallRisk = (int)Math.Round(overallRisk),
            OpportunityScore = (int)Math.Round(opportunityScore),
            TrendDirection = trend,
            HealthScore = (int)Math.Round(healthScore),
            UsageScore = (int)Math.Round(usageScore),
            NewsMomentum = (int)Math.Round(newsMomentum),
            LastUpdated = supporting.Max(f => f.Created),
            SupportingFacts = supporting
                .OrderByDescending(f => f.Importance)
                .ThenByDescending(f => f.Confidence)
                .ThenBy(f => f.Id)
                .ToList(),
            Headline = HeadlineFor(signal),
            ChangeSignal = signal
        };
    }

    private static IReadOnlyList<IntelligenceFact> DedupeFacts(IReadOnlyList<IntelligenceFact> facts)
    {
        // Keep the strongest fact per (category + rule id).
        return facts
            .GroupBy(f => $"{f.Category}|{ExtractRuleId(f) ?? f.Title}", StringComparer.Ordinal)
            .Select(g => g
                .OrderByDescending(f => f.Importance)
                .ThenByDescending(f => f.Confidence)
                .ThenBy(f => f.Id)
                .First())
            .OrderBy(f => f.Id)
            .ToList();
    }

    private static string? ExtractRuleId(IntelligenceFact fact)
    {
        var line = fact.SupportingEvidence.FirstOrDefault(e => e.StartsWith("Rule:", StringComparison.OrdinalIgnoreCase));
        if (line is null)
        {
            return null;
        }

        return line.Replace("Rule:", "", StringComparison.OrdinalIgnoreCase).Trim();
    }

    private double ImportanceWeight(IntelligenceImportance importance) => importance switch
    {
        IntelligenceImportance.Low => _scoring.ImportanceWeightLow,
        IntelligenceImportance.Medium => _scoring.ImportanceWeightMedium,
        IntelligenceImportance.High => _scoring.ImportanceWeightHigh,
        IntelligenceImportance.Critical => _scoring.ImportanceWeightCritical,
        _ => _scoring.ImportanceWeightMedium
    };

    private static void ApplyCategoryFallback(
        IntelligenceCategory category,
        double weight,
        ref double health,
        ref double opportunity,
        ref double usage,
        ref double risk)
    {
        switch (category)
        {
            case IntelligenceCategory.Injury:
            case IntelligenceCategory.Suspension:
                health -= 15 * weight;
                risk += 12 * weight;
                break;
            case IntelligenceCategory.Opportunity:
                opportunity += 12 * weight;
                break;
            case IntelligenceCategory.Usage:
                usage += 10 * weight;
                break;
            case IntelligenceCategory.Transaction:
                opportunity += 6 * weight;
                break;
            case IntelligenceCategory.Coaching:
                usage += 6 * weight;
                break;
        }
    }

    private static TrendDirection InferTrend(
        double health,
        double opportunity,
        double usage,
        double risk,
        int baseline)
    {
        var upside = (opportunity - baseline) + (usage - baseline) + Math.Max(0, health - baseline);
        var downside = (baseline - health) + risk + Math.Max(0, baseline - opportunity);
        if (upside > downside + 8)
        {
            return TrendDirection.Up;
        }

        if (downside > upside + 8)
        {
            return TrendDirection.Down;
        }

        return TrendDirection.Flat;
    }

    private static IntelligenceChangeSignal InferSignal(
        double health,
        double opportunity,
        double usage,
        double risk,
        int baseline)
    {
        var healthDelta = health - baseline;
        var opportunityDelta = opportunity - baseline;
        var usageDelta = usage - baseline;

        if (healthDelta <= -15 || risk >= 40)
        {
            return IntelligenceChangeSignal.HealthConcern;
        }

        if (opportunityDelta <= -12)
        {
            return IntelligenceChangeSignal.OpportunityDecreasing;
        }

        if (risk >= 25 && healthDelta < 0)
        {
            return IntelligenceChangeSignal.ElevatedRisk;
        }

        if (opportunityDelta >= 12)
        {
            return IntelligenceChangeSignal.OpportunityIncreasing;
        }

        if (usageDelta >= 10)
        {
            return IntelligenceChangeSignal.UsageIncreasing;
        }

        if (healthDelta >= 10)
        {
            return IntelligenceChangeSignal.HealthImproving;
        }

        return IntelligenceChangeSignal.Neutral;
    }

    private static string HeadlineFor(IntelligenceChangeSignal signal) => signal switch
    {
        IntelligenceChangeSignal.OpportunityIncreasing => "Opportunity Increasing",
        IntelligenceChangeSignal.UsageIncreasing => "Usage Increasing",
        IntelligenceChangeSignal.HealthImproving => "Health Improving",
        IntelligenceChangeSignal.HealthConcern => "Health Concern",
        IntelligenceChangeSignal.OpportunityDecreasing => "Opportunity Decreasing",
        IntelligenceChangeSignal.ElevatedRisk => "Elevated Risk",
        _ => "Stable Outlook"
    };

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
}
