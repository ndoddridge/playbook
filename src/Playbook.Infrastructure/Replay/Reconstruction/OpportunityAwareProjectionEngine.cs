using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Reconstruction;

/// <summary>
/// Baseline B (primary): weighted recent production blended with opportunity/usage context.
/// Still a transparent historical baseline — not a trained model.
/// </summary>
public sealed class OpportunityAwareProjectionEngine : IHistoricalProjectionEngine
{
    public const string Id = "baseline-opportunity-aware-v1";

    public string ModelId => Id;

    public string ModelLabel => "Baseline B · Opportunity-aware";

    public HistoricalProjection Project(HistoricalPlayerFeatures features, ScoringType scoringType)
    {
        _ = scoringType;

        if (features.Sufficiency == DataSufficiency.Insufficient ||
            features.FantasyPointsPerGame is null)
        {
            return Invalid(features, "No prior games available before cutoff.");
        }

        var last1 = features.Last1FantasyPoints ?? features.FantasyPointsPerGame.Value;
        var last3 = features.Last3FantasyPointsAvg ?? features.FantasyPointsPerGame.Value;
        var seasonToDate = features.FantasyPointsPerGame.Value;

        // Heavier weight on recent form; season-to-date stabilizes short samples.
        var production = features.GamesUsed switch
        {
            1 => last1,
            2 => (0.65 * last1) + (0.35 * seasonToDate),
            _ => (0.45 * last1) + (0.35 * last3) + (0.20 * seasonToDate)
        };

        var opportunityFactor = (features.OpportunityScore ?? 50) / 50.0; // 1.0 at neutral 50
        var usageFactor = (features.UsageScore ?? features.OpportunityScore ?? 50) / 50.0;
        var blendedFactor = (0.65 * opportunityFactor) + (0.35 * usageFactor);
        blendedFactor = Math.Clamp(blendedFactor, 0.75, 1.25);

        // Mild position dampening so extreme opportunity scores don't explode early-season noise.
        var positionScale = features.Position switch
        {
            Position.QB => 1.00,
            Position.RB => 1.00,
            Position.WR => 0.98,
            Position.TE => 0.96,
            _ => 0.95
        };

        var projected = production * blendedFactor * positionScale;
        var volatility = features.Sufficiency == DataSufficiency.Limited ? 0.55 : 0.40;
        var floor = Math.Max(0, projected * (1 - volatility));
        var ceiling = projected * (1 + volatility + 0.15);

        var confidence = features.FeatureConfidence;
        if (features.OpportunityScore is null)
        {
            confidence -= 8;
        }

        if (features.AvgOffenseSnapPct is null)
        {
            confidence -= 4;
        }

        confidence = features.Sufficiency switch
        {
            DataSufficiency.Sufficient => Math.Clamp(confidence, 45, 88),
            DataSufficiency.Limited => Math.Clamp(confidence - 10, 22, 58),
            _ => 12
        };

        return new HistoricalProjection
        {
            ModelId = ModelId,
            ModelLabel = ModelLabel,
            ProjectedPoints = Math.Round(projected, 1, MidpointRounding.AwayFromZero),
            Floor = Math.Round(floor, 1, MidpointRounding.AwayFromZero),
            Ceiling = Math.Round(ceiling, 1, MidpointRounding.AwayFromZero),
            ProjectionConfidence = confidence,
            Sufficiency = features.Sufficiency,
            SourceWeeks = features.SourceWeeks,
            Methodology =
                $"Weighted prior fantasy production (L1/L3/STD) × opportunity/usage factor " +
                $"from weeks [{string.Join(',', features.SourceWeeks)}] only; " +
                $"excludes week {features.TargetWeek} actuals. " +
                $"Opp={features.OpportunityScore?.ToString() ?? "n/a"}, " +
                $"Usage={features.UsageScore?.ToString() ?? "n/a"}, " +
                $"sufficiency={features.Sufficiency}."
        };
    }

    private HistoricalProjection Invalid(HistoricalPlayerFeatures features, string reason) =>
        new()
        {
            ModelId = ModelId,
            ModelLabel = ModelLabel,
            ProjectedPoints = 0,
            Floor = null,
            Ceiling = null,
            ProjectionConfidence = 10,
            Sufficiency = DataSufficiency.Insufficient,
            SourceWeeks = features.SourceWeeks,
            Methodology = reason
        };
}
