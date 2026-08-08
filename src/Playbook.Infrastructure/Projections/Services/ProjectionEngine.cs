using Microsoft.Extensions.Options;
using Playbook.Application.Injuries;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Projections.Services;

/// <summary>
/// Player-specific Projection Engine.
/// Baseline = production-derived weekly fantasy points (scoring-aware).
/// Intelligence adjusts volume / downside / ceiling — never invents identical baselines.
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
        PlayerProductionSnapshot production,
        PlayerIntelligenceProfile? intelligence,
        ProjectionLeagueContext leagueContext,
        PlayerInjuryRecord? currentInjury = null)
    {
        var reasoning = new List<string>();
        var supporting = new List<string>();

        var scoring = leagueContext.ScoringType;
        var weeklyBase = FantasyScoring.WeeklyFantasyPoints(production, scoring);
        var seasonPoints = FantasyScoring.SeasonFantasyPoints(production, scoring);

        reasoning.Add(
            $"Base projection: {weeklyBase:0.0} {FormatScoring(scoring)} points from {production.Season} production " +
            $"({seasonPoints:0.0} season pts / {Math.Max(1, production.GamesPlayed)} games).");
        reasoning.Add(production.SourceDescription);
        reasoning.AddRange(FantasyScoring.DescribeComponents(production, scoring));

        var health = intelligence?.HealthScore ?? 50;
        var opportunity = intelligence?.OpportunityScore ?? 50;
        var usage = intelligence?.UsageScore ?? 50;
        var risk = intelligence?.OverallRisk ?? 0;
        var trend = intelligence?.TrendDirection ?? TrendDirection.Flat;
        var intelConfidence = intelligence?.OverallConfidence ?? 45;

        // Volume multipliers from opportunity / usage (centered at 50).
        var opportunityFactor = 1m + ScoreDeltaFromNeutral(opportunity, _rules.OpportunityVolumeFactor);
        var usageFactor = 1m + ScoreDeltaFromNeutral(usage, _rules.UsageVolumeFactor);

        if (opportunity != 50)
        {
            reasoning.Add(opportunity > 50
                ? $"Opportunity score {opportunity} increased expected volume (×{opportunityFactor:0.00})."
                : $"Opportunity score {opportunity} decreased expected volume (×{opportunityFactor:0.00}).");
        }

        if (usage != 50)
        {
            reasoning.Add(usage > 50
                ? $"Usage score {usage} increased expected involvement (×{usageFactor:0.00})."
                : $"Usage score {usage} decreased expected involvement (×{usageFactor:0.00}).");
        }

        var volumeAdjusted = weeklyBase * opportunityFactor * usageFactor;

        // Health: downside when poor; small upside when strong.
        decimal healthFactor = 1m;
        if (health < 50)
        {
            healthFactor = 1m - ((50 - health) / 50m) * _rules.HealthDownsideFactor;
            reasoning.Add(
                $"Health score {health} produces downside adjustment (×{healthFactor:0.00}).");
        }
        else if (health > 50)
        {
            healthFactor = 1m + ((health - 50) / 50m) * _rules.HealthUpsideFactor;
            reasoning.Add(
                $"Health score {health} produces minimal downside / slight upside (×{healthFactor:0.00}).");
        }

        var riskFactor = 1m - (risk / 100m) * _rules.RiskDownsideFactor;
        if (risk > 0)
        {
            reasoning.Add($"Risk {risk} trims projection (×{riskFactor:0.00}).");
        }

        var trendFactor = trend switch
        {
            TrendDirection.Up => 1m + _rules.TrendFactor,
            TrendDirection.Down => 1m - _rules.TrendFactor,
            _ => 1m
        };
        if (trend == TrendDirection.Up)
        {
            reasoning.Add("Recent usage/opportunity trend increased projection and ceiling.");
        }
        else if (trend == TrendDirection.Down)
        {
            reasoning.Add("Negative usage/opportunity trend decreased projection.");
        }

        if (intelligence is null)
        {
            reasoning.Add("No PlayerIntelligenceProfile — neutral intelligence assumptions applied.");
        }

        var medianRaw = volumeAdjusted * healthFactor * riskFactor * trendFactor;

        // Conservative availability clamp from structured current injury (Out/IR/etc.).
        // Keeps major health designations from projecting as if fully healthy.
        var injuryMultiplier = InjuryIntelligenceMapping.ProjectionHealthMultiplier(currentInjury);
        if (injuryMultiplier is decimal injuryFactor)
        {
            medianRaw *= injuryFactor;
            reasoning.Add(
                $"Current injury status '{currentInjury!.Status}' applies conservative availability factor " +
                $"(×{injuryFactor:0.00})" +
                (string.IsNullOrWhiteSpace(currentInjury.BodyPart) ? "." : $" for {currentInjury.BodyPart}."));
        }

        var median = Clamp(Round1(medianRaw));
        var projected = median;

        var usageCeilingBonus = usage > 50
            ? median * ScoreDeltaFromNeutral(usage, _rules.UsageCeilingFactor)
            : 0m;
        if (usageCeilingBonus >= 0.05m)
        {
            reasoning.Add($"Strong usage trend raised ceiling by {usageCeilingBonus:0.0}.");
        }

        var productionConfidence = production.Source switch
        {
            ProductionDataSource.CuratedSeason => 78,
            ProductionDataSource.ProfileSeason => 70,
            _ => 48
        };
        var confidence = (int)Math.Round(
            intelConfidence * _rules.IntelligenceConfidenceWeight +
            productionConfidence * (1 - _rules.IntelligenceConfidenceWeight));
        confidence = Math.Clamp(confidence, 0, 100);

        var volatility = ComputeVolatility(confidence, health, risk, usage, production.Source);
        if (intelConfidence < 55 || production.Source == ProductionDataSource.AttributeFallback)
        {
            reasoning.Add(
                $"Low input confidence (intel {intelConfidence}%, production source {production.Source}) " +
                $"increases volatility to {volatility}.");
        }
        else
        {
            reasoning.Add($"High intelligence confidence {intelConfidence}% reduces projection uncertainty (volatility {volatility}).");
        }

        var volFactor = volatility / 100m;
        var healthFloorPenalty = health < 50 ? ((50m - health) / 50m) * 2.2m : 0m;
        var floorSpread = _rules.BaseFloorSpread + (volFactor * 5.5m) + healthFloorPenalty;
        var ceilingSpread = _rules.BaseCeilingSpread + (volFactor * 4.2m) + usageCeilingBonus;
        if (trend == TrendDirection.Up)
        {
            ceilingSpread += median * (_rules.TrendFactor * 0.75m);
        }

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

        // Guarantee strict ordering when spreads collapse on clamps.
        if (floor >= median && median > _rules.MinProjection)
        {
            floor = Clamp(Round1(median - 0.1m));
        }

        if (ceiling <= median && median < _rules.MaxProjection)
        {
            ceiling = Clamp(Round1(median + 0.1m));
        }

        reasoning.Add(
            $"League scoring: {FormatScoring(scoring)} · {leagueContext.LeagueName} · Week {leagueContext.CurrentWeek}.");

        AppendSupportingIntelligence(intelligence, production, supporting);

        return new PlayerProjection
        {
            PlayerId = player.Id,
            ProjectedFantasyPoints = projected,
            Floor = floor,
            Median = median,
            Ceiling = ceiling,
            Confidence = confidence,
            Volatility = volatility,
            ProjectionReasoning = reasoning,
            SupportingIntelligence = supporting,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    public IReadOnlyList<PlayerProjection> ProjectMany(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Guid, PlayerProductionSnapshot> productionByPlayer,
        IReadOnlyDictionary<Guid, PlayerIntelligenceProfile> intelligenceByPlayer,
        ProjectionLeagueContext leagueContext,
        IReadOnlyDictionary<Guid, PlayerInjuryRecord>? currentInjuriesByPlayer = null)
    {
        var results = new List<PlayerProjection>(players.Count);
        foreach (var player in players.OrderBy(p => p.Id))
        {
            if (!productionByPlayer.TryGetValue(player.Id, out var production))
            {
                continue;
            }

            intelligenceByPlayer.TryGetValue(player.Id, out var profile);
            PlayerInjuryRecord? injury = null;
            currentInjuriesByPlayer?.TryGetValue(player.Id, out injury);
            results.Add(Project(player, production, profile, leagueContext, injury));
        }

        return results;
    }

    private int ComputeVolatility(
        int confidence,
        int health,
        int risk,
        int usage,
        ProductionDataSource source)
    {
        var fromConfidence = (100 - confidence) * _rules.VolatilityFromLowConfidence;
        var fromHealth = Math.Max(0, 50 - health) * 0.35;
        var fromRisk = risk * 0.20;
        var fromUsageSwing = Math.Abs(usage - 50) * 0.12;
        var fromSource = source == ProductionDataSource.AttributeFallback ? 12 : 0;
        var raw = _rules.BaselineVolatility + fromConfidence + fromHealth + fromRisk + fromUsageSwing + fromSource;
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
        PlayerProductionSnapshot production,
        List<string> supporting)
    {
        supporting.Add(
            $"Production[{production.Source}] {production.Season}: " +
            $"Pass {production.PassingYards}/{production.PassingTouchdowns}TD/{production.Interceptions}INT · " +
            $"Rush {production.RushingYards}/{production.RushingTouchdowns}TD · " +
            $"Rec {production.Receptions}/{production.ReceivingYards}/{production.ReceivingTouchdowns}TD · " +
            $"Tgt {production.Targets}");

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
                     .Take(5))
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
