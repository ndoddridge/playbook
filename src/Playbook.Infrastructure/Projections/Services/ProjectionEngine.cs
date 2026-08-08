using Microsoft.Extensions.Options;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Projections.Services;

/// <summary>
/// Weighted, deterministic Projection Engine V1.
/// Estimates expected fantasy outcomes only — never start/sit or roster advice.
/// </summary>
public sealed class ProjectionEngine : IProjectionEngine
{
    private readonly ProjectionRuleOptions _rules;

    public ProjectionEngine(IOptions<ProjectionRuleOptions> rules)
    {
        _rules = rules.Value;
    }

    public PlayerProjection Project(
        Player player,
        PlayerIntelligenceProfile? intelligence,
        ProjectionLeagueContext leagueContext)
    {
        var reasoning = new List<string>();
        var supporting = new List<string>();

        var baseline = ResolveBaseline(player.Position);
        var scoringBoost = ResolveScoringBoost(player.Position, leagueContext.ScoringType);
        var basePoints = baseline + scoringBoost;

        reasoning.Add(
            $"Base {player.Position} projection {baseline:0.0} pts" +
            (scoringBoost == 0
                ? $" ({leagueContext.ScoringType} scoring)."
                : $" + {scoringBoost:0.0} for {FormatScoring(leagueContext.ScoringType)}."));

        var health = intelligence?.HealthScore ?? 50;
        var opportunity = intelligence?.OpportunityScore ?? 50;
        var usage = intelligence?.UsageScore ?? 50;
        var risk = intelligence?.OverallRisk ?? 50;
        var momentum = intelligence?.NewsMomentum ?? 50;
        var intelConfidence = intelligence?.OverallConfidence ?? 40;

        // Health / Opportunity / Usage are centered at 50 (neutral).
        var healthAdj = ScoreDeltaFromNeutral(health, _rules.HealthWeight);
        var opportunityAdj = ScoreDeltaFromNeutral(opportunity, _rules.OpportunityWeight);
        var usageMedianAdj = ScoreDeltaFromNeutral(usage, _rules.UsageWeight) * 0.55m;
        // Risk and momentum are 0-based (0 = none); they do not use a 50 baseline.
        var riskAdj = -(risk / 100m) * _rules.RiskWeight;
        var momentumAdj = (momentum / 100m) * _rules.MomentumWeight * 0.35m;

        if (healthAdj != 0)
        {
            reasoning.Add(healthAdj > 0
                ? $"Higher Health ({health}) raises projection by {healthAdj:0.0}."
                : $"Negative injury / Health ({health}) lowers projection by {Math.Abs(healthAdj):0.0}.");
        }

        if (opportunityAdj != 0)
        {
            reasoning.Add(opportunityAdj > 0
                ? $"Higher Opportunity ({opportunity}) increases projection by {opportunityAdj:0.0}."
                : $"Lower Opportunity ({opportunity}) decreases projection by {Math.Abs(opportunityAdj):0.0}.");
        }

        if (usageMedianAdj != 0)
        {
            reasoning.Add(usageMedianAdj > 0
                ? $"Positive Usage ({usage}) lifts median by {usageMedianAdj:0.0}."
                : $"Soft Usage ({usage}) trims median by {Math.Abs(usageMedianAdj):0.0}.");
        }

        if (riskAdj < 0)
        {
            reasoning.Add($"Elevated Risk ({risk}) reduces projection by {Math.Abs(riskAdj):0.0}.");
        }

        if (Math.Abs(momentumAdj) >= 0.05m)
        {
            reasoning.Add($"News Momentum ({momentum}) lifts median by {momentumAdj:0.0}.");
        }

        if (intelligence is null)
        {
            reasoning.Add("No PlayerIntelligenceProfile — neutral intelligence assumptions (50).");
        }

        var medianRaw = basePoints + healthAdj + opportunityAdj + usageMedianAdj + riskAdj + momentumAdj;
        var median = Clamp(Round1(medianRaw));
        var projected = median;

        var usageCeilingBonus = usage > 50
            ? ScoreDeltaFromNeutral(usage, _rules.UsageCeilingBonus)
            : 0m;
        if (usageCeilingBonus > 0)
        {
            reasoning.Add($"Positive Usage raises ceiling by {usageCeilingBonus:0.0}.");
        }

        var volatility = ComputeVolatility(intelConfidence, health, risk, usage);
        if (intelConfidence < 55)
        {
            reasoning.Add($"Low Confidence ({intelConfidence}%) increases volatility to {volatility}.");
        }
        else
        {
            reasoning.Add($"Projection confidence {intelConfidence}% → volatility {volatility}.");
        }

        var volFactor = volatility / 100m;
        var healthFloorPenalty = health < 50 ? ((50 - health) / 50m) * 1.5m : 0m;
        var floorSpread = _rules.BaseFloorSpread + (volFactor * 5.0m) + healthFloorPenalty;
        var ceilingSpread = _rules.BaseCeilingSpread + (volFactor * 4.0m) + usageCeilingBonus;

        var floor = Clamp(Round1(median - floorSpread));
        var ceiling = Clamp(Round1(median + ceilingSpread));
        if (floor > median)
        {
            floor = median;
        }

        if (ceiling < median)
        {
            ceiling = median;
        }

        reasoning.Add(
            $"League context: {leagueContext.LeagueName}, Week {leagueContext.CurrentWeek}, " +
            $"{leagueContext.NumberOfTeams}-team, {FormatScoring(leagueContext.ScoringType)}.");

        AppendSupportingIntelligence(intelligence, supporting);

        return new PlayerProjection
        {
            PlayerId = player.Id,
            ProjectedFantasyPoints = projected,
            Floor = floor,
            Median = median,
            Ceiling = ceiling,
            Confidence = Math.Clamp(intelConfidence, 0, 100),
            Volatility = volatility,
            ProjectionReasoning = reasoning,
            SupportingIntelligence = supporting,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    public IReadOnlyList<PlayerProjection> ProjectMany(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Guid, PlayerIntelligenceProfile> intelligenceByPlayer,
        ProjectionLeagueContext leagueContext)
    {
        var results = new List<PlayerProjection>(players.Count);
        foreach (var player in players.OrderBy(p => p.Id))
        {
            intelligenceByPlayer.TryGetValue(player.Id, out var profile);
            results.Add(Project(player, profile, leagueContext));
        }

        return results;
    }

    private decimal ResolveBaseline(Position position)
    {
        var key = position.ToString();
        if (_rules.PositionBaselines.TryGetValue(key, out var value))
        {
            return value;
        }

        return 8.0m;
    }

    private decimal ResolveScoringBoost(Position position, ScoringType scoring)
    {
        var key = position.ToString();
        return scoring switch
        {
            ScoringType.HalfPpr => _rules.HalfPprBoosts.TryGetValue(key, out var half) ? half : 0m,
            ScoringType.Ppr => _rules.PprBoosts.TryGetValue(key, out var ppr) ? ppr : 0m,
            _ => 0m
        };
    }

    private int ComputeVolatility(int confidence, int health, int risk, int usage)
    {
        var fromConfidence = (100 - confidence) * _rules.VolatilityFromLowConfidence;
        var fromHealth = Math.Abs(health - 50) * 0.25;
        var fromRisk = Math.Max(0, risk - 50) * 0.35;
        var fromUsageSwing = Math.Abs(usage - 50) * 0.15;
        var raw = _rules.BaselineVolatility + fromConfidence + fromHealth + fromRisk + fromUsageSwing;
        return Math.Clamp((int)Math.Round(raw), 5, 95);
    }

    private decimal Clamp(decimal value) =>
        Math.Clamp(value, _rules.MinProjection, _rules.MaxProjection);

    private static decimal ScoreDeltaFromNeutral(int score, decimal weight) =>
        ((score - 50m) / 50m) * weight;

    private static decimal Round1(decimal value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static void AppendSupportingIntelligence(
        PlayerIntelligenceProfile? intelligence,
        List<string> supporting)
    {
        if (intelligence is null)
        {
            supporting.Add("Neutral intelligence defaults (no profile).");
            return;
        }

        supporting.Add($"Headline: {intelligence.Headline}");
        supporting.Add(
            $"Scores — Health {intelligence.HealthScore}, Opportunity {intelligence.OpportunityScore}, " +
            $"Usage {intelligence.UsageScore}, Risk {intelligence.OverallRisk}, Momentum {intelligence.NewsMomentum}");
        supporting.Add($"Overall Confidence {intelligence.OverallConfidence}% · Trend {intelligence.TrendDirection}");

        foreach (var fact in intelligence.SupportingFacts
                     .OrderByDescending(f => f.Importance)
                     .ThenByDescending(f => f.Confidence)
                     .Take(6))
        {
            supporting.Add($"{fact.Category}: {fact.Title} ({fact.Confidence}%)");
        }
    }

    private static string FormatScoring(ScoringType scoring) => scoring switch
    {
        ScoringType.Ppr => "PPR",
        ScoringType.HalfPpr => "Half PPR",
        ScoringType.Standard => "Standard",
        _ => scoring.ToString()
    };
}
