using Playbook.Core.Intelligence.Models;
using Playbook.Core.Predictions;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Estimates football counting-stat expectations for prop markets.
/// Uses production + recent usage — not fantasy scoring settings.
/// Preseason slates treat regular-season production as a prior only (lower confidence).
/// </summary>
public static class PropStatProjector
{
    public static (decimal? Projection, int Confidence, int Volatility, bool UsingPriorRegularSeasonProduction) Project(
        PredictionMarketType market,
        PlayerProductionSnapshot? production,
        PlayerStatisticalContext? stats,
        PlayerIntelligenceProfile? intelligence,
        NflSeasonPhase seasonPhase = NflSeasonPhase.RegularSeason)
    {
        var usingPrior = false;

        if (market is PredictionMarketType.GameTotal or PredictionMarketType.TeamTotal
            or PredictionMarketType.Winner or PredictionMarketType.Spread)
        {
            // Game markets have no real team/game projection input: IMatchupContextProvider and
            // IGameEnvironmentProvider are both registered as "Unavailable" (see
            // DependencyInjection.RegisterProjections) — there is no legitimate signal behind a
            // number here, for any team, in any season phase. A fixed constant compared against a
            // real sportsbook line would look like genuine intelligence when it is not tied to the
            // actual matchup at all, so this reports "no projection" (insufficient data) rather
            // than fabricating one. QuickPicksEngine excludes markets with no projection.
            return (null, 0, 100, false);
        }

        if (production is null)
        {
            return (null, 25, 70, false);
        }

        var games = Math.Max(1, production.GamesPlayed);
        decimal PerGame(decimal total) => Math.Round(total / games, 1, MidpointRounding.AwayFromZero);

        var seasonValue = market switch
        {
            PredictionMarketType.PassingYards => PerGame(production.PassingYards),
            PredictionMarketType.RushingYards => PerGame(production.RushingYards),
            PredictionMarketType.ReceivingYards => PerGame(production.ReceivingYards),
            PredictionMarketType.Receptions => PerGame(production.Receptions),
            PredictionMarketType.PassingTouchdowns => PerGame(production.PassingTouchdowns),
            PredictionMarketType.AnytimeTouchdown => PerGame(
                production.RushingTouchdowns + production.ReceivingTouchdowns +
                (production.Position == Core.Players.Position.QB ? production.PassingTouchdowns * 0.15m : 0)),
            _ => (decimal?)null
        };

        if (seasonValue is null)
        {
            return (null, 30, 65, false);
        }

        var projected = seasonValue.Value;
        var confidence = production.Source switch
        {
            ProductionDataSource.StatsService => 72,
            ProductionDataSource.CuratedSeason => 68,
            ProductionDataSource.ProfileSeason => 64,
            _ => 45
        };
        var volatility = 40;

        // Preseason: regular-season (or prior-year) production is a prior only.
        // Do not let a full REG season pace mint high confidence against preseason lines.
        if (seasonPhase == NflSeasonPhase.Preseason)
        {
            usingPrior = true;
            // Soften counting-stat pace slightly — preseason workloads differ — but keep directionality.
            projected = Math.Round(projected * 0.85m, 1, MidpointRounding.AwayFromZero);
            confidence = Math.Min(confidence, 48);
            volatility = Math.Max(volatility, 58);
        }

        if (stats?.RecentProduction?.PerGame is { } recent &&
            stats.RecentProduction.Games is int rg &&
            rg >= 2)
        {
            if (seasonPhase == NflSeasonPhase.Preseason)
            {
                // Recent REG games are still historical priors in preseason — light blend only.
                var recentValue = market switch
                {
                    PredictionMarketType.PassingYards => recent.PassYards ?? projected,
                    PredictionMarketType.RushingYards => recent.RushYards ?? projected,
                    PredictionMarketType.ReceivingYards => recent.ReceivingYards ?? projected,
                    PredictionMarketType.Receptions => recent.Receptions ?? projected,
                    PredictionMarketType.PassingTouchdowns => recent.PassTouchdowns ?? projected,
                    PredictionMarketType.AnytimeTouchdown =>
                        (recent.RushTouchdowns ?? 0) + (recent.ReceivingTouchdowns ?? 0),
                    _ => projected
                };
                projected = Math.Round(projected * 0.75m + recentValue * 0.25m, 1, MidpointRounding.AwayFromZero);
                confidence = Math.Min(confidence + 2, 50);
            }
            else
            {
                var recentValue = market switch
                {
                    PredictionMarketType.PassingYards => recent.PassYards ?? projected,
                    PredictionMarketType.RushingYards => recent.RushYards ?? projected,
                    PredictionMarketType.ReceivingYards => recent.ReceivingYards ?? projected,
                    PredictionMarketType.Receptions => recent.Receptions ?? projected,
                    PredictionMarketType.PassingTouchdowns => recent.PassTouchdowns ?? projected,
                    PredictionMarketType.AnytimeTouchdown =>
                        (recent.RushTouchdowns ?? 0) + (recent.ReceivingTouchdowns ?? 0),
                    _ => projected
                };
                projected = Math.Round(projected * 0.45m + recentValue * 0.55m, 1, MidpointRounding.AwayFromZero);
                confidence += 6;
                if (stats.VolatilityScore is decimal volScore)
                {
                    volatility = (int)Math.Clamp(volScore, 20, 90);
                }
            }
        }

        if (intelligence is not null)
        {
            // Usage / opportunity nudge counting-stat outlook (current intel remains valuable in preseason).
            var usageTilt = (intelligence.UsageScore - 50) / 50m * 0.10m;
            var oppTilt = (intelligence.OpportunityScore - 50) / 50m * 0.09m;
            projected = Math.Round(projected * (1 + usageTilt + oppTilt), 1, MidpointRounding.AwayFromZero);

            if (seasonPhase == NflSeasonPhase.Preseason)
            {
                // In preseason, lean more on current intelligence confidence than historical production.
                confidence = (int)Math.Round(confidence * 0.40 + intelligence.OverallConfidence * 0.60);
                confidence = Math.Min(confidence, 62);
            }
            else
            {
                confidence = (int)Math.Round(confidence * 0.55 + intelligence.OverallConfidence * 0.45);
            }

            if (intelligence.HealthScore < 40)
            {
                projected = Math.Round(projected * 0.85m, 1, MidpointRounding.AwayFromZero);
                confidence -= 12;
                volatility += 15;
            }
            else if (intelligence.HealthScore > 70)
            {
                confidence += 4;
            }
        }

        if (market == PredictionMarketType.AnytimeTouchdown)
        {
            projected = Math.Clamp(projected, 0m, 1.8m);
        }

        return (
            projected,
            Math.Clamp(confidence, 15, 95),
            Math.Clamp(volatility, 15, 95),
            usingPrior);
    }
}
