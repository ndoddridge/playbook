using Microsoft.Extensions.Options;
using Playbook.Application.Injuries;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Projections.Services;

/// <summary>
/// Projection Engine v0.1 — deterministic, explainable weekly fantasy outcomes.
/// Consumes normalized statistics + intelligence; does not scrape providers or parse news.
/// </summary>
public sealed class ProjectionEngine : IProjectionEngine
{
    private readonly ProjectionRuleOptions _rules;

    public ProjectionEngine(IOptions<ProjectionRuleOptions> rules)
    {
        _rules = rules.Value;
    }

    public string Version => ProjectionEngineVersions.Current;

    public PlayerProjection Project(
        Player player,
        PlayerProductionSnapshot production,
        PlayerIntelligenceProfile? intelligence,
        ProjectionLeagueContext leagueContext,
        PlayerInjuryRecord? currentInjury = null,
        PlayerStatisticalContext? statisticalContext = null,
        MatchupContext? matchup = null,
        GameEnvironmentContext? gameEnvironment = null)
    {
        matchup ??= MatchupContext.Unavailable();
        gameEnvironment ??= GameEnvironmentContext.Unavailable();

        var scoring = leagueContext.ScoringType;
        var reasoning = new List<string>();
        var supporting = new List<string>();
        var unavailable = new List<string>();
        var notes = new List<string>();

        var (baseWeekly, baselineMethod, usedRecent, usedCareer, usedCollege) =
            BuildBaseline(player, production, statisticalContext, scoring, reasoning, notes);

        var health = intelligence?.HealthScore ?? 50;
        var opportunity = intelligence?.OpportunityScore ?? 50;
        var usageScore = intelligence?.UsageScore ?? 50;
        var risk = intelligence?.OverallRisk ?? 0;
        var momentum = intelligence?.NewsMomentum ?? 50;
        var trend = ResolveTrend(intelligence, statisticalContext);
        var intelConfidence = intelligence?.OverallConfidence ?? 45;

        // Explicit separate signals with point deltas (explainable).
        var projected = baseWeekly;

        var usageDelta = ComputeCenteredPointDelta(baseWeekly, usageScore, _rules.UsageVolumeFactor);
        projected += usageDelta;
        reasoning.Add(ExplainDelta("Recent usage", usageDelta, $"usage score {usageScore}"));

        var opportunityDelta = ComputeCenteredPointDelta(baseWeekly, opportunity, _rules.OpportunityVolumeFactor);
        projected += opportunityDelta;
        reasoning.Add(ExplainDelta("Opportunity", opportunityDelta, $"opportunity score {opportunity}"));

        var recencyDelta = ComputeRecencyDelta(baseWeekly, statisticalContext, production);
        projected += recencyDelta;
        reasoning.Add(ExplainDelta("Recency", recencyDelta, DescribeRecency(statisticalContext, production)));

        var trendDelta = ComputeTrendDelta(baseWeekly, trend);
        projected += trendDelta;
        reasoning.Add(ExplainDelta("Opportunity trend", trendDelta, DescribeTrend(trend)));

        var healthDelta = ComputeHealthDelta(baseWeekly, health);
        projected += healthDelta;
        reasoning.Add(health == 50
            ? "Health: looks about average for this week."
            : ExplainDelta("Health", healthDelta, $"health score {health}"));

        var riskDelta = -Round1(baseWeekly * (risk / 100m) * _rules.RiskDownsideFactor);
        projected += riskDelta;
        if (risk > 0)
        {
            reasoning.Add(ExplainDelta("Risk", riskDelta, $"risk {risk}"));
        }

        var momentumDelta = ComputeCenteredPointDelta(baseWeekly, momentum, _rules.MomentumFactor);
        if (Math.Abs(momentumDelta) >= 0.05m)
        {
            projected += momentumDelta;
            reasoning.Add(ExplainDelta("Momentum", momentumDelta, $"news momentum {momentum}"));
        }

        var injuryMultiplier = InjuryIntelligenceMapping.ProjectionHealthMultiplier(currentInjury);
        if (injuryMultiplier is decimal injuryFactor)
        {
            var before = projected;
            projected *= injuryFactor;
            reasoning.Add(
                $"Injury signal: status '{currentInjury!.Status}' scales the projection ×{injuryFactor:0.00} " +
                $"({before:0.0} → {projected:0.0})" +
                (string.IsNullOrWhiteSpace(currentInjury.BodyPart) ? "" : $" · {currentInjury.BodyPart}"));
        }
        else
        {
            reasoning.Add("Injury signal: nothing currently limiting the projection.");
        }

        var matchupDelta = 0m;
        if (matchup.IsAvailable)
        {
            matchupDelta = ComputeMatchupDelta(matchup);
            projected += matchupDelta;
            reasoning.Add(ExplainDelta("Matchup", matchupDelta, matchup.Summary ?? "available"));
        }
        else
        {
            unavailable.Add("Matchup");
            reasoning.Add("Matchup: unavailable — not factored into this projection yet.");
        }

        var environmentDelta = 0m;
        if (gameEnvironment.IsAvailable)
        {
            environmentDelta = ComputeEnvironmentDelta(gameEnvironment);
            projected += environmentDelta;
            reasoning.Add(ExplainDelta("Game environment", environmentDelta, gameEnvironment.Summary ?? "available"));
        }
        else
        {
            unavailable.Add("Game environment");
            reasoning.Add("Game environment: unavailable — weather/pace context not applied yet.");
        }

        if (intelligence is null)
        {
            reasoning.Add("Intelligence profile unavailable, so neutral intelligence assumptions were applied.");
            unavailable.Add("IntelligenceProfile");
        }

        var median = Clamp(Round1(projected));

        var productionConfidence = production.Source switch
        {
            ProductionDataSource.StatsService => usedRecent ? 82 : 74,
            ProductionDataSource.CuratedSeason => 70,
            ProductionDataSource.ProfileSeason => 66,
            _ => 42
        };

        var samplePenalty = SampleSizeConfidencePenalty(player, production, statisticalContext);
        var roleCertainty = RoleCertaintyBoost(usageScore, opportunity, trend);
        var confidence = (int)Math.Round(
            intelConfidence * _rules.IntelligenceConfidenceWeight +
            productionConfidence * (1 - _rules.IntelligenceConfidenceWeight) +
            roleCertainty -
            samplePenalty);
        confidence = Math.Clamp(confidence, 15, 95);

        var volatility = ComputeVolatility(
            confidence,
            health,
            risk,
            usageScore,
            production,
            statisticalContext,
            currentInjury,
            trend);
        reasoning.Add($"Confidence: about {confidence}% after weighing sample size, role certainty, and intelligence.");
        reasoning.Add($"Volatility: {volatility} — used to shape the floor/ceiling range.");
        reasoning.Add(
            $"League scoring: {FormatScoring(scoring)} in {leagueContext.LeagueName}, week {leagueContext.CurrentWeek}.");
        reasoning.Add($"{ProjectionEngineVersions.DisplayName}");

        var (floor, ceiling) = ComputeRange(median, volatility, health, usageScore, trend, currentInjury);
        AppendSupportingIntelligence(intelligence, production, statisticalContext, supporting);

        var inputs = new ProjectionInputsUsed
        {
            HistoricalStatistics = production.Source is ProductionDataSource.StatsService
                or ProductionDataSource.CuratedSeason
                or ProductionDataSource.ProfileSeason,
            RecentUsage = usedRecent || Math.Abs(usageDelta) >= 0.05m,
            CareerBaseline = usedCareer,
            CollegeStatistics = usedCollege,
            IntelligenceProfile = intelligence is not null,
            InjurySignal = currentInjury is not null,
            MatchupContext = matchup.IsAvailable,
            GameEnvironment = gameEnvironment.IsAvailable,
            LeagueScoring = true,
            ProductionSource = production.Source.ToString(),
            BaselineMethod = baselineMethod,
            Notes = notes,
            UnavailableInputs = unavailable
        };

        return new PlayerProjection
        {
            PlayerId = player.Id,
            LeagueId = leagueContext.LeagueId,
            Week = leagueContext.CurrentWeek,
            ScoringFormat = scoring,
            ProjectedFantasyPoints = median,
            Floor = floor,
            Median = median,
            Ceiling = ceiling,
            Confidence = confidence,
            Volatility = volatility,
            ProjectionReasoning = reasoning,
            SupportingIntelligence = supporting,
            ProjectionTimestamp = DateTimeOffset.UtcNow,
            ProjectionVersion = Version,
            InputsUsed = inputs
        };
    }

    public IReadOnlyList<PlayerProjection> ProjectMany(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Guid, PlayerProductionSnapshot> productionByPlayer,
        IReadOnlyDictionary<Guid, PlayerIntelligenceProfile> intelligenceByPlayer,
        ProjectionLeagueContext leagueContext,
        IReadOnlyDictionary<Guid, PlayerInjuryRecord>? currentInjuriesByPlayer = null,
        IReadOnlyDictionary<Guid, PlayerStatisticalContext>? statisticalContextByPlayer = null,
        IReadOnlyDictionary<Guid, MatchupContext>? matchupByPlayer = null,
        IReadOnlyDictionary<Guid, GameEnvironmentContext>? environmentByPlayer = null)
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
            PlayerStatisticalContext? statsCtx = null;
            statisticalContextByPlayer?.TryGetValue(player.Id, out statsCtx);
            MatchupContext? matchup = null;
            matchupByPlayer?.TryGetValue(player.Id, out matchup);
            GameEnvironmentContext? env = null;
            environmentByPlayer?.TryGetValue(player.Id, out env);

            results.Add(Project(player, production, profile, leagueContext, injury, statsCtx, matchup, env));
        }

        return results;
    }

    private (decimal Base, string Method, bool UsedRecent, bool UsedCareer, bool UsedCollege) BuildBaseline(
        Player player,
        PlayerProductionSnapshot production,
        PlayerStatisticalContext? stats,
        ScoringType scoring,
        List<string> reasoning,
        List<string> notes)
    {
        var seasonWeekly = FantasyScoring.WeeklyFantasyPoints(production, scoring);
        var components = new List<(string Label, decimal Points, decimal Weight)>();

        // Primary: production snapshot (already prefers current/recent NFL season).
        components.Add(("season production", seasonWeekly, 1.0m));

        decimal? recentWeekly = null;
        if (stats?.RecentProduction?.PerGame is { } recentPerGame &&
            stats.RecentProduction.Games is int rg &&
            rg >= 2)
        {
            recentWeekly = LeagueFantasyScoring.Calculate(recentPerGame, scoring);
            var recentWeight = rg >= _rules.StrongRecentSampleGames ? 1.35m : 0.85m;
            components.Add(($"recent {rg} NFL games", recentWeekly.Value, recentWeight));
            notes.Add("Recent game-log production weighted above equal season averaging.");
        }

        if (stats?.HistoricalProduction?.PerGame is { } histPerGame &&
            stats.HistoricalProduction.Games is int hg &&
            hg >= 8)
        {
            var histWeekly = LeagueFantasyScoring.Calculate(histPerGame, scoring);
            components.Add(("multi-season NFL history", histWeekly, 0.55m));
        }

        var usedCareer = false;
        if (stats?.CareerBaseline?.PerGame is { } careerPerGame &&
            stats.CareerBaseline.Games is int cg &&
            cg >= 16)
        {
            var careerWeekly = LeagueFantasyScoring.Calculate(careerPerGame, scoring);
            components.Add(("NFL career baseline", careerWeekly, 0.35m));
            usedCareer = true;
        }

        var usedCollege = false;
        var yearsPro = player.YearsPro ?? 99;
        if (yearsPro < 3 &&
            stats?.CollegeProduction?.PerGame is { } collegePerGame &&
            stats.HasCollegeStatistics)
        {
            var collegeWeekly = LeagueFantasyScoring.Calculate(collegePerGame, scoring);
            // College is evidence, not an NFL sample — discounted hard.
            var collegeWeight = yearsPro switch
            {
                0 => 0.45m,
                1 => 0.28m,
                _ => 0.18m
            };
            components.Add(("college production (separate from NFL)", collegeWeekly, collegeWeight));
            usedCollege = true;
            notes.Add("College evidence incorporated at reduced weight; not mixed into NFL averages.");
            reasoning.Add(
                $"College evidence is available (YearsPro={yearsPro}) and weighted lightly at {collegeWeight:0.00}, kept separate from the NFL sample.");
        }

        var weightSum = components.Sum(c => c.Weight);
        var blended = weightSum <= 0
            ? seasonWeekly
            : components.Sum(c => c.Points * c.Weight) / weightSum;

        var usedRecent = recentWeekly is not null;
        var method = usedRecent
            ? "weighted recent NFL games + season/history"
            : usedCollege
                ? "limited NFL sample + discounted college"
                : "season production baseline";

        reasoning.Add($"Base projection: starting from {Round1(blended):0.0} points under this league's scoring.");
        reasoning.Add(
            $"Baseline method: {method} from {production.Season} {production.Source} " +
            $"({string.Join("; ", components.Select(c => $"{c.Label}={c.Points:0.0}×{c.Weight:0.00}"))}).");
        reasoning.Add(production.SourceDescription);
        reasoning.AddRange(FantasyScoring.DescribeComponents(production, scoring));

        return (Round1(blended), method, usedRecent, usedCareer, usedCollege);
    }

    private decimal ComputeRecencyDelta(
        decimal baseWeekly,
        PlayerStatisticalContext? stats,
        PlayerProductionSnapshot production)
    {
        if (stats?.RecentProduction?.PerGame is null || stats.RecentProduction.Games is not int g || g < 2)
        {
            // Soft nudge when production is current-season labeled.
            return production.SourceDescription.Contains("CurrentSeason", StringComparison.OrdinalIgnoreCase) ||
                   production.SourceDescription.Contains("Real current", StringComparison.OrdinalIgnoreCase)
                ? Round1(baseWeekly * (_rules.RecencyFactor * 0.25m))
                : 0m;
        }

        // Representativeness: more recent games → stronger pull already in baseline;
        // residual recency delta rewards larger recent samples slightly.
        var strength = Math.Min(1m, g / (decimal)_rules.StrongRecentSampleGames);
        return Round1(baseWeekly * _rules.RecencyFactor * 0.35m * strength);
    }

    private decimal ComputeTrendDelta(decimal baseWeekly, StatisticalTrendSignal trend) =>
        trend switch
        {
            StatisticalTrendSignal.Increasing => Round1(baseWeekly * _rules.TrendFactor),
            StatisticalTrendSignal.Decreasing => Round1(-baseWeekly * _rules.TrendFactor),
            StatisticalTrendSignal.Volatile => Round1(-baseWeekly * (_rules.TrendFactor * 0.25m)),
            _ => 0m
        };

    private decimal ComputeHealthDelta(decimal baseWeekly, int health)
    {
        if (health < 50)
        {
            return Round1(-baseWeekly * ((50 - health) / 50m) * _rules.HealthDownsideFactor);
        }

        if (health > 50)
        {
            return Round1(baseWeekly * ((health - 50) / 50m) * _rules.HealthUpsideFactor);
        }

        return 0m;
    }

    private decimal ComputeMatchupDelta(MatchupContext matchup)
    {
        // Positive OpponentDefenseStrength = tougher defense → negative projection swing.
        var defense = matchup.OpponentDefenseStrength ?? 0m;
        var positionPerf = matchup.OpponentPositionPerformance ?? 0m;
        // positionPerf: above 0 means opponent allows more fantasy points.
        var combined = (-defense * 0.6m) + (positionPerf * 0.4m);
        return Round1(Math.Clamp(combined, -1m, 1m) * _rules.MatchupMaxSwing);
    }

    private decimal ComputeEnvironmentDelta(GameEnvironmentContext env)
    {
        var swing = 0m;
        if (env.TeamImpliedTotal is decimal teamTotal)
        {
            // League-averageish ~22; scale softly.
            swing += Math.Clamp((teamTotal - 22m) / 10m, -1m, 1m) * 0.5m;
        }

        if (env.ExpectedPace is decimal pace)
        {
            swing += Math.Clamp((pace - 1m), -0.5m, 0.5m) * 0.4m;
        }

        if (env.IsHome is true)
        {
            swing += 0.15m;
        }
        else if (env.IsHome is false)
        {
            swing -= 0.10m;
        }

        return Round1(Math.Clamp(swing, -1m, 1m) * _rules.EnvironmentMaxSwing);
    }

    private (decimal Floor, decimal Ceiling) ComputeRange(
        decimal median,
        int volatility,
        int health,
        int usage,
        StatisticalTrendSignal trend,
        PlayerInjuryRecord? injury)
    {
        var vol = volatility / 100m;
        var floorFrac = _rules.FloorSigmaMin + (_rules.FloorSigmaMax - _rules.FloorSigmaMin) * vol;
        var ceilFrac = _rules.CeilingSigmaMin + (_rules.CeilingSigmaMax - _rules.CeilingSigmaMin) * vol;

        // Absolute minimum spreads so low medians still have a band.
        var floorSpread = Math.Max(0.8m, Round1(median * floorFrac));
        var ceilingSpread = Math.Max(1.0m, Round1(median * ceilFrac));

        if (health < 50)
        {
            floorSpread += Round1(((50 - health) / 50m) * 2.0m);
        }

        if (usage > 55 || trend == StatisticalTrendSignal.Increasing)
        {
            ceilingSpread += Round1(median * 0.06m);
        }

        if (trend == StatisticalTrendSignal.Decreasing || trend == StatisticalTrendSignal.Volatile)
        {
            floorSpread += Round1(median * 0.04m);
        }

        if (injury is not null && IsElevatedInjuryStatus(injury.Status))
        {
            floorSpread += 1.5m;
        }

        var floor = Clamp(Round1(median - floorSpread));
        var ceiling = Clamp(Round1(median + ceilingSpread));

        if (floor >= median && median > _rules.MinProjection)
        {
            floor = Clamp(Round1(median - 0.1m));
        }

        if (ceiling <= median && median < _rules.MaxProjection)
        {
            ceiling = Clamp(Round1(median + 0.1m));
        }

        if (floor > median)
        {
            floor = median;
        }

        if (ceiling < median)
        {
            ceiling = median;
        }

        return (floor, ceiling);
    }

    private int ComputeVolatility(
        int confidence,
        int health,
        int risk,
        int usage,
        PlayerProductionSnapshot production,
        PlayerStatisticalContext? stats,
        PlayerInjuryRecord? injury,
        StatisticalTrendSignal trend)
    {
        var fromConfidence = (100 - confidence) * _rules.VolatilityFromLowConfidence;
        var fromHealth = Math.Max(0, 50 - health) * 0.40;
        var fromRisk = risk * 0.22;
        var fromUsageSwing = Math.Abs(usage - 50) * 0.14;
        var fromSource = production.Source == ProductionDataSource.AttributeFallback ? 14 : 0;
        var fromTdDependency = TdDependencyBonus(production);
        var fromSample = stats?.GameLogsAvailable is int g && g < 4 ? 10 : 0;
        var fromTrend = trend switch
        {
            StatisticalTrendSignal.Volatile => 12,
            StatisticalTrendSignal.Increasing or StatisticalTrendSignal.Decreasing => 6,
            _ => 0
        };
        var fromInjury = InjuryVolatilityBonus(injury?.Status);
        var fromConsistency = stats?.Consistency?.CoefficientOfVariation is decimal cov
            ? (double)Math.Clamp(cov * 40m, 0, 20)
            : 0;

        var raw = _rules.BaselineVolatility + fromConfidence + fromHealth + fromRisk + fromUsageSwing +
                  fromSource + fromTdDependency + fromSample + fromTrend + fromInjury + fromConsistency;
        return Math.Clamp((int)Math.Round(raw), 5, 95);
    }

    private static bool IsElevatedInjuryStatus(string? status) =>
        EqualsStatus(status, "Questionable", "Doubtful", "Out", "IR", "Injured Reserve");

    private static int InjuryVolatilityBonus(string? status)
    {
        if (EqualsStatus(status, "Questionable"))
        {
            return 8;
        }

        if (EqualsStatus(status, "Doubtful"))
        {
            return 14;
        }

        if (EqualsStatus(status, "Out", "IR", "Injured Reserve"))
        {
            return 10;
        }

        return 0;
    }

    private static bool EqualsStatus(string? status, params string[] options) =>
        options.Any(o => string.Equals(status?.Trim(), o, StringComparison.OrdinalIgnoreCase));

    private static double TdDependencyBonus(PlayerProductionSnapshot production)
    {
        var games = Math.Max(1, production.GamesPlayed);
        var yards = production.RushingYards + production.ReceivingYards + (production.PassingYards / 4);
        var tds = production.RushingTouchdowns + production.ReceivingTouchdowns + production.PassingTouchdowns;
        if (yards <= 0)
        {
            return tds > 0 ? 8 : 0;
        }

        var yardsPerGame = yards / (decimal)games;
        var tdsPerGame = tds / (decimal)games;
        // Heavy TD share vs yardage → boom/bust.
        return tdsPerGame >= 0.7m && yardsPerGame < 60 ? 10 : tdsPerGame >= 0.5m ? 5 : 0;
    }

    private static int SampleSizeConfidencePenalty(
        Player player,
        PlayerProductionSnapshot production,
        PlayerStatisticalContext? stats)
    {
        var years = player.YearsPro ?? 0;
        var games = stats?.GameLogsAvailable ?? production.GamesPlayed;
        var penalty = 0;
        if (years < 2)
        {
            penalty += 12;
        }
        else if (years < 3)
        {
            penalty += 6;
        }

        if (games < 4)
        {
            penalty += 10;
        }
        else if (games < 8)
        {
            penalty += 5;
        }

        if (production.Source == ProductionDataSource.AttributeFallback)
        {
            penalty += 15;
        }

        return penalty;
    }

    private static int RoleCertaintyBoost(int usage, int opportunity, StatisticalTrendSignal trend)
    {
        var stable = Math.Abs(usage - 50) < 12 && Math.Abs(opportunity - 50) < 12;
        if (stable && trend is StatisticalTrendSignal.Stable or StatisticalTrendSignal.Unknown)
        {
            return 6;
        }

        if (trend is StatisticalTrendSignal.Volatile)
        {
            return -4;
        }

        return 0;
    }

    private static StatisticalTrendSignal ResolveTrend(
        PlayerIntelligenceProfile? intelligence,
        PlayerStatisticalContext? stats)
    {
        if (stats is not null && stats.Trend != StatisticalTrendSignal.Unknown)
        {
            return stats.Trend;
        }

        return intelligence?.TrendDirection switch
        {
            TrendDirection.Up => StatisticalTrendSignal.Increasing,
            TrendDirection.Down => StatisticalTrendSignal.Decreasing,
            TrendDirection.Flat => StatisticalTrendSignal.Stable,
            _ => StatisticalTrendSignal.Unknown
        };
    }

    private static decimal ComputeCenteredPointDelta(decimal baseWeekly, int score, decimal factor) =>
        Round1(baseWeekly * ((score - 50m) / 50m) * factor);

    private decimal Clamp(decimal value) =>
        Math.Clamp(value, _rules.MinProjection, _rules.MaxProjection);

    private static decimal Round1(decimal value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static string ExplainDelta(string label, decimal delta, string detail)
    {
        if (Math.Abs(delta) < 0.05m)
        {
            return $"{label}: looks about neutral ({detail}).";
        }

        var direction = delta > 0 ? "lifts" : "trims";
        var sign = delta > 0 ? "+" : string.Empty;
        return $"{label}: {direction} the projection by {sign}{delta:0.0} ({detail}).";
    }

    private static string DescribeRecency(PlayerStatisticalContext? stats, PlayerProductionSnapshot production)
    {
        if (stats?.RecentProduction?.Games is int g)
        {
            return $"{g} recent NFL games";
        }

        return production.SourceDescription.Contains("current", StringComparison.OrdinalIgnoreCase)
            ? "current-season emphasis"
            : "limited recent sample";
    }

    private static string DescribeTrend(StatisticalTrendSignal trend) => trend switch
    {
        StatisticalTrendSignal.Increasing => "increasing workload/production",
        StatisticalTrendSignal.Decreasing => "decreasing workload/production",
        StatisticalTrendSignal.Volatile => "volatile recent pattern",
        StatisticalTrendSignal.Stable => "stable recent pattern",
        _ => "trend unknown"
    };

    private static void AppendSupportingIntelligence(
        PlayerIntelligenceProfile? intelligence,
        PlayerProductionSnapshot production,
        PlayerStatisticalContext? stats,
        List<string> supporting)
    {
        supporting.Add(
            $"Production[{production.Source}] {production.Season}: " +
            $"Pass {production.PassingYards}/{production.PassingTouchdowns}TD/{production.Interceptions}INT · " +
            $"Rush {production.RushingYards}/{production.RushingTouchdowns}TD · " +
            $"Rec {production.Receptions}/{production.ReceivingYards}/{production.ReceivingTouchdowns}TD · " +
            $"Tgt {production.Targets}");

        if (stats is not null)
        {
            supporting.Add(
                $"Statistical context — NFL seasons {stats.NflSeasonsAvailable}, game logs {stats.GameLogsAvailable}, " +
                $"trend {stats.Trend}, college={stats.HasCollegeStatistics}");
            if (stats.Usage is { } usage)
            {
                supporting.Add(
                    $"Usage signals — carries/g {usage.CarriesPerGame?.ToString("0.0") ?? "—"}, " +
                    $"targets/g {usage.TargetsPerGame?.ToString("0.0") ?? "—"}, " +
                    $"workload {usage.WorkloadTrend ?? "—"}");
            }
        }

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
