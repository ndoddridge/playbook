using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Application.Projections;

/// <summary>
/// Deterministic fantasy-point calculator from production components.
/// </summary>
public static class FantasyScoring
{
    public static decimal SeasonFantasyPoints(PlayerProductionSnapshot production, ScoringType scoring)
    {
        if (production.SpecialistWeeklyPrior is decimal weekly && production.GamesPlayed > 0)
        {
            return weekly * production.GamesPlayed;
        }

        if (production.SpecialistWeeklyPrior is decimal weeklyOnly)
        {
            return weeklyOnly * 17m;
        }

        var points = 0m;
        points += production.PassingYards / 25m;
        points += production.PassingTouchdowns * 4m;
        points -= production.Interceptions * 2m;
        points += production.RushingYards / 10m;
        points += production.RushingTouchdowns * 6m;
        points += production.ReceivingYards / 10m;
        points += production.ReceivingTouchdowns * 6m;

        var receptionPoints = scoring switch
        {
            ScoringType.Ppr => production.Receptions * 1.0m,
            ScoringType.HalfPpr => production.Receptions * 0.5m,
            _ => 0m
        };
        points += receptionPoints;

        return Math.Round(points, 1, MidpointRounding.AwayFromZero);
    }

    public static decimal WeeklyFantasyPoints(PlayerProductionSnapshot production, ScoringType scoring)
    {
        if (production.SpecialistWeeklyPrior is decimal weekly)
        {
            return Math.Round(weekly, 1, MidpointRounding.AwayFromZero);
        }

        var games = Math.Max(1, production.GamesPlayed);
        return Math.Round(SeasonFantasyPoints(production, scoring) / games, 1, MidpointRounding.AwayFromZero);
    }

    public static IReadOnlyList<string> DescribeComponents(
        PlayerProductionSnapshot production,
        ScoringType scoring)
    {
        var lines = new List<string>();
        switch (production.Position)
        {
            case Position.QB:
                lines.Add(
                    $"Passing: {production.PassingYards} yds, {production.PassingTouchdowns} TD, " +
                    $"{production.Interceptions} INT across {Math.Max(1, production.GamesPlayed)} games.");
                lines.Add(
                    $"Rushing contribution: {production.RushingYards} yds, {production.RushingTouchdowns} TD.");
                break;
            case Position.RB:
                lines.Add(
                    $"Rushing: {production.RushingYards} yds, {production.RushingTouchdowns} TD" +
                    (production.RushingAttempts > 0 ? $" on {production.RushingAttempts} carries." : "."));
                lines.Add(
                    $"Receiving: {production.Targets} tgt / {production.Receptions} rec / " +
                    $"{production.ReceivingYards} yds / {production.ReceivingTouchdowns} TD.");
                break;
            case Position.WR:
            case Position.TE:
                lines.Add(
                    $"Receiving: {production.Targets} tgt / {production.Receptions} rec / " +
                    $"{production.ReceivingYards} yds / {production.ReceivingTouchdowns} TD " +
                    $"({Math.Max(1, production.GamesPlayed)} games).");
                if (production.RushingYards > 0 || production.RushingTouchdowns > 0)
                {
                    lines.Add(
                        $"Rushing: {production.RushingYards} yds, {production.RushingTouchdowns} TD.");
                }

                break;
            default:
                lines.Add($"Specialist weekly prior {production.SpecialistWeeklyPrior:0.0} ({scoring}).");
                break;
        }

        return lines;
    }
}
