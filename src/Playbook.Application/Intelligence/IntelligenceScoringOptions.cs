using Playbook.Core.Intelligence.Models;

namespace Playbook.Application.Intelligence;

/// <summary>
/// Centralized, configurable score deltas applied during aggregation.
/// Bound from <c>Intelligence:Scoring</c>.
/// </summary>
public sealed class IntelligenceScoringOptions
{
    public const string SectionName = "Intelligence:Scoring";

    public int BaselineScore { get; set; } = 50;

    public List<ScoreRuleOptions> Rules { get; set; } =
    [
        new() { RuleId = "injury-limited", HealthDelta = -25 },
        new() { RuleId = "injury-questionable", HealthDelta = -20 },
        new() { RuleId = "injury-doubtful", HealthDelta = -30 },
        new() { RuleId = "injury-out", HealthDelta = -40, RiskDelta = 25 },
        new() { RuleId = "injury-ir", HealthDelta = -45, RiskDelta = 30 },
        new() { RuleId = "injury-positive", HealthDelta = 15 },
        new() { RuleId = "suspension", HealthDelta = -35, RiskDelta = 30, OpportunityDelta = -10 },
        new() { RuleId = "opportunity-start", OpportunityDelta = 20, UsageDelta = 10 },
        new() { RuleId = "usage-first-team", UsageDelta = 15, OpportunityDelta = 10 },
        new() { RuleId = "usage-snap", UsageDelta = 12 },
        new() { RuleId = "depth-chart", OpportunityDelta = 8, UsageDelta = 5 },
        new() { RuleId = "transaction-signed", OpportunityDelta = -15 },
        new() { RuleId = "transaction-released", OpportunityDelta = 12 },
        new() { RuleId = "transaction-trade", OpportunityDelta = 10, UsageDelta = 5 },
        new() { RuleId = "coaching", UsageDelta = 10 },
        new() { RuleId = "contract", OpportunityDelta = 5 },
        new() { RuleId = "weather", RiskDelta = 8, HealthDelta = -5 },
        new() { RuleId = "practice-camp", MomentumDelta = 5 }
    ];

    public double ImportanceWeightLow { get; set; } = 0.5;
    public double ImportanceWeightMedium { get; set; } = 1.0;
    public double ImportanceWeightHigh { get; set; } = 1.5;
    public double ImportanceWeightCritical { get; set; } = 2.0;
}

public sealed class ScoreRuleOptions
{
    public string RuleId { get; set; } = string.Empty;
    public int HealthDelta { get; set; }
    public int OpportunityDelta { get; set; }
    public int UsageDelta { get; set; }
    public int RiskDelta { get; set; }
    public int MomentumDelta { get; set; }
}
