using Playbook.Core.Leagues;
using Playbook.Core.Stats.Models;

namespace Playbook.Application.Stats;

/// <summary>
/// Calculates fantasy points from canonical football statistics and a league's scoring type.
/// Does not store three unrelated fantasy datasets — one football sample, many league translations.
/// Missing (null) counting fields contribute nothing; zero fields contribute zero.
/// </summary>
public static class LeagueFantasyScoring
{
    public static decimal Calculate(CanonicalCountingStats stats, ScoringType scoring)
    {
        var points = 0m;

        // Null contributes nothing; explicit zero contributes zero.
        points += (stats.PassYards ?? 0) / 25m;
        points += (stats.PassTouchdowns ?? 0) * 4m;
        points -= (stats.PassInterceptions ?? 0) * 2m;
        points += (stats.RushYards ?? 0) / 10m;
        points += (stats.RushTouchdowns ?? 0) * 6m;
        points += (stats.ReceivingYards ?? 0) / 10m;
        points += (stats.ReceivingTouchdowns ?? 0) * 6m;

        var receptions = stats.Receptions ?? 0;
        points += scoring switch
        {
            ScoringType.Ppr => receptions * 1.0m,
            ScoringType.HalfPpr => receptions * 0.5m,
            _ => 0m
        };

        // Structural K/DST hooks (detail arrives later).
        points += (stats.FieldGoalsMade ?? 0) * 3m;
        points += (stats.ExtraPointsMade ?? 0) * 1m;
        points += (stats.DefensiveTouchdowns ?? 0) * 6m;
        points += (stats.Sacks ?? 0) * 1m;
        points += (stats.Safeties ?? 0) * 2m;

        return Math.Round(points, 1, MidpointRounding.AwayFromZero);
    }

    public static decimal Calculate(PlayerSeasonStats season, ScoringType scoring) =>
        Calculate(season.ToCountingStats(), scoring);

    public static decimal Calculate(PlayerGameStats game, ScoringType scoring) =>
        Calculate(game.ToCountingStats(), scoring);

    public static (decimal Standard, decimal HalfPpr, decimal Ppr) CalculateAll(CanonicalCountingStats stats) =>
        (
            Calculate(stats, ScoringType.Standard),
            Calculate(stats, ScoringType.HalfPpr),
            Calculate(stats, ScoringType.Ppr)
        );
}
