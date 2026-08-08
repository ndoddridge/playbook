using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;

namespace Playbook.Application.Projections;

/// <summary>
/// Human-readable baseline origin for Projection UI — distinguishes real vs curated vs fallback.
/// </summary>
public static class ProductionBaselineLabels
{
    public static string Describe(PlayerProductionSnapshot production) =>
        production.Source switch
        {
            ProductionDataSource.StatsService when production.SourceDescription.Contains(
                "CurrentSeason", StringComparison.OrdinalIgnoreCase)
                || production.SourceDescription.Contains("current", StringComparison.OrdinalIgnoreCase)
                => "Real current data",
            ProductionDataSource.StatsService => "Real historical data",
            ProductionDataSource.CuratedSeason => "Curated data",
            ProductionDataSource.ProfileSeason => "Real historical data",
            ProductionDataSource.AttributeFallback => "Fallback estimate (no box-score stats)",
            _ => production.Source.ToString()
        };

    public static string DescribeFromSeason(PlayerSeasonStats? season) =>
        season switch
        {
            null => "No statistical baseline",
            { Period: StatsPeriod.CurrentSeason } => "Real current data",
            { Period: StatsPeriod.CompletedSeason } => "Real historical data",
            { Period: StatsPeriod.College } => "College data (separate from NFL)",
            { Period: StatsPeriod.Career } => "Real historical data",
            _ when string.Equals(season.SourceProvider, "Mock", StringComparison.OrdinalIgnoreCase)
                => "Mock data",
            _ => "Real historical data"
        };
}
