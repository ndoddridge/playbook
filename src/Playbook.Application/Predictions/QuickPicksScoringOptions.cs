namespace Playbook.Application.Predictions;

/// <summary>
/// Tunable Quick Picks intelligence weights (Engine v0.3).
/// Bound from <c>QuickPicks:Scoring</c> so weights can change without UI rewrites.
/// </summary>
public sealed class QuickPicksScoringOptions
{
    public const string SectionName = "QuickPicks:Scoring";

    /// <summary>When player intelligence profile is missing (player props).</summary>
    public decimal MissingIntelligenceQualityFactor { get; set; } = 0.82m;

    public int MissingIntelligenceConfidencePenalty { get; set; } = 10;

    /// <summary>Quality factor when preseason uses prior regular-season production.</summary>
    public decimal PreseasonPriorProductionQualityFactor { get; set; } = 0.78m;

    /// <summary>Confidence penalty applied on preseason slates.</summary>
    public int PreseasonConfidencePenalty { get; set; } = 8;

    /// <summary>Weight of OverallConfidence into quality (0–1).</summary>
    public decimal IntelligenceConfidenceWeight { get; set; } = 0.35m;

    public decimal HealthScoreWeight { get; set; } = 1.0m;

    public decimal UsageSignalWeight { get; set; } = 0.55m;

    public decimal OpportunitySignalWeight { get; set; } = 0.45m;

    /// <summary>Current verified injury influence (much stronger than historical).</summary>
    public decimal CurrentInjuryWeight { get; set; } = 1.0m;

    /// <summary>Historical injury influence cap (High/Moderate relevance only).</summary>
    public decimal HistoricalInjuryWeight { get; set; } = 0.22m;

    /// <summary>Age decay: each year multiplies historical influence by (1 − this).</summary>
    public decimal HistoricalInjuryDecayPerYear { get; set; } = 0.55m;

    /// <summary>Unconfirmed buzz — soft drag only; never treated as verified fact.</summary>
    public decimal UnconfirmedSignalWeight { get; set; } = 0.28m;

    public int UnconfirmedConfidencePenalty { get; set; } = 6;

    public decimal UnconfirmedMaxQualityDrag { get; set; } = 0.12m;

    /// <summary>Single news/fact cannot move quality by more than this.</summary>
    public decimal MaxSingleNewsQualityDrag { get; set; } = 0.06m;

    public decimal NewsSignalWeight { get; set; } = 0.18m;

    public decimal SmallDiffScaleFraction { get; set; } = 0.08m;

    public decimal LowQualityDampener { get; set; } = 0.35m;

    public decimal LowQualityThreshold { get; set; } = 0.25m;

    public decimal ProbabilityEdgeScale { get; set; } = 28m;

    public int StaleConfidencePenalty { get; set; } = 18;

    public int StaleProbabilityPenalty { get; set; } = 6;

    /// <summary>Extra edge bias (in market-scale units) against Overs when currently Out/IR.</summary>
    public decimal SevereInjuryOverBiasScale { get; set; } = 0.85m;
}
