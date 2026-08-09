using Playbook.Application.Replay;
using Playbook.Core.Players;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay.Reconstruction;

/// <summary>
/// Reconstructs pre-week features from prior REG games only.
/// Rejects any observation whose week is &gt;= target week.
/// </summary>
public sealed class HistoricalFeatureReconstructor : IHistoricalFeatureReconstructor
{
    public HistoricalPlayerFeatures Reconstruct(
        Guid playerId,
        string playerName,
        Position position,
        string team,
        int season,
        int targetWeek,
        DateTimeOffset informationCutoff,
        IReadOnlyList<HistoricalGameObservation> priorGames,
        string? roleNote = null)
    {
        // Absolute temporal guard: never accept target week or later.
        var safe = priorGames
            .Where(g => g.Season < season || (g.Season == season && g.Week < targetWeek))
            .OrderBy(g => g.Season)
            .ThenBy(g => g.Week)
            .ToList();

        if (priorGames.Any(g => g.Season > season || (g.Season == season && g.Week >= targetWeek)))
        {
            // Soft filter already applied; keep an explicit invariant for callers that pass dirty lists.
            safe = priorGames
                .Where(g => g.Season < season || (g.Season == season && g.Week < targetWeek))
                .OrderBy(g => g.Season)
                .ThenBy(g => g.Week)
                .ToList();
        }

        var unavailable = new List<string>
        {
            "Historical news (unavailable)",
            "Target share (unavailable in current reconstruction)",
            "Red-zone usage (unavailable in current reconstruction)"
        };

        if (safe.Count == 0)
        {
            return new HistoricalPlayerFeatures
            {
                PlayerId = playerId,
                PlayerName = playerName,
                Position = position,
                Team = team,
                Season = season,
                TargetWeek = targetWeek,
                InformationCutoff = informationCutoff,
                SourceWeeks = [],
                GamesUsed = 0,
                Sufficiency = DataSufficiency.Insufficient,
                RoleNote = roleNote,
                UnavailableFeatures = unavailable.Concat(["Recent production", "Opportunity", "Usage", "Rolling fantasy windows"]).ToList(),
                FeatureConfidence = 12
            };
        }

        var sourceWeeks = safe.Select(g => g.Week).Distinct().OrderBy(w => w).ToList();
        var sufficiency = safe.Count >= 3
            ? DataSufficiency.Sufficient
            : DataSufficiency.Limited;

        var last = safe[^1];
        var last3 = safe.TakeLast(Math.Min(3, safe.Count)).ToList();
        var last5 = safe.TakeLast(Math.Min(5, safe.Count)).ToList();

        if (safe.All(g => g.OffenseSnapPct is null))
        {
            unavailable.Add("Snap percentage");
        }

        var avgTargets = Avg(safe, g => g.Targets);
        var avgCarries = Avg(safe, g => g.RushAttempts);
        var avgPassAtt = Avg(safe, g => g.PassAttempts);
        var avgSnaps = AvgDouble(safe, g => g.OffenseSnapPct);

        var opportunity = DeriveOpportunity(position, avgPassAtt, avgCarries, avgTargets);
        var usage = avgSnaps is double snap
            ? (int)Math.Clamp(Math.Round(snap * 100.0), 0, 100)
            : opportunity;
        var fppg = safe.Average(g => g.FantasyPoints);
        var recentProduction = (int)Math.Clamp(Math.Round(fppg * 5.0), 0, 100);

        var featureConfidence = sufficiency switch
        {
            DataSufficiency.Sufficient => Math.Clamp(55 + safe.Count * 4, 55, 85),
            DataSufficiency.Limited => Math.Clamp(30 + safe.Count * 8, 25, 55),
            _ => 12
        };

        if (opportunity is null)
        {
            unavailable.Add("Opportunity");
        }

        return new HistoricalPlayerFeatures
        {
            PlayerId = playerId,
            PlayerName = playerName,
            Position = position,
            Team = team,
            Season = season,
            TargetWeek = targetWeek,
            InformationCutoff = informationCutoff,
            SourceWeeks = sourceWeeks,
            GamesUsed = safe.Count,
            Sufficiency = sufficiency,
            FantasyPointsPerGame = Round(fppg),
            Last1FantasyPoints = Round(last.FantasyPoints),
            Last3FantasyPointsAvg = Round(last3.Average(g => g.FantasyPoints)),
            Last5FantasyPointsAvg = Round(last5.Average(g => g.FantasyPoints)),
            AvgTargets = avgTargets,
            AvgReceptions = Avg(safe, g => g.Receptions),
            AvgRushAttempts = avgCarries,
            AvgPassAttempts = avgPassAtt,
            AvgTouches = Avg(safe, g => (g.RushAttempts ?? 0) + (g.Receptions ?? 0)),
            AvgReceivingYards = Avg(safe, g => g.ReceivingYards),
            AvgRushYards = Avg(safe, g => g.RushYards),
            AvgPassYards = Avg(safe, g => g.PassYards),
            AvgTouchdowns = Avg(safe, g =>
                (g.PassTouchdowns ?? 0) + (g.RushTouchdowns ?? 0) + (g.ReceivingTouchdowns ?? 0)),
            AvgOffenseSnapPct = avgSnaps is null ? null : Round(avgSnaps.Value * 100.0),
            OpportunityScore = opportunity,
            UsageScore = usage,
            RecentProductionScore = recentProduction,
            RoleNote = roleNote,
            UnavailableFeatures = unavailable.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            FeatureConfidence = featureConfidence
        };
    }

    private static int? DeriveOpportunity(
        Position position,
        double? avgPassAtt,
        double? avgCarries,
        double? avgTargets)
    {
        double? raw = position switch
        {
            Position.QB => avgPassAtt,
            Position.RB => (avgCarries ?? 0) + (avgTargets ?? 0),
            Position.WR or Position.TE => avgTargets,
            _ => null
        };

        if (raw is null)
        {
            return null;
        }

        return position switch
        {
            Position.QB => Scale(raw.Value, 20, 40),
            Position.RB => Scale(raw.Value, 8, 22),
            Position.WR => Scale(raw.Value, 4, 12),
            Position.TE => Scale(raw.Value, 3, 9),
            _ => 50
        };
    }

    private static int Scale(double value, double low, double high)
    {
        if (high <= low)
        {
            return 50;
        }

        var t = (value - low) / (high - low);
        return (int)Math.Clamp(Math.Round(35 + t * 50), 5, 95);
    }

    private static double? Avg(IReadOnlyList<HistoricalGameObservation> games, Func<HistoricalGameObservation, int?> selector)
    {
        var values = games.Select(selector).Where(v => v is not null).Select(v => (double)v!.Value).ToList();
        return values.Count == 0 ? null : Round(values.Average());
    }

    private static double? AvgDouble(IReadOnlyList<HistoricalGameObservation> games, Func<HistoricalGameObservation, double?> selector)
    {
        var values = games.Select(selector).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Average();
    }

    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
