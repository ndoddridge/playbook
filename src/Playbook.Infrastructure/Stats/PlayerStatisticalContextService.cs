using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Builds Intelligence-facing statistical signals from the normalized stats store.
/// Not the final Intelligence Engine — only the clean data interface it will consume.
/// </summary>
public sealed class PlayerStatisticalContextService : IPlayerStatisticalContextService
{
    private readonly IPlayerStatsService _stats;

    public PlayerStatisticalContextService(IPlayerStatsService stats)
    {
        _stats = stats;
    }

    public PlayerStatisticalContext? GetContext(Guid playerId)
    {
        var seasons = _stats.GetStatsForPlayer(playerId)
            .Where(r => r.Period != StatsPeriod.Career)
            .ToList();
        if (seasons.Count == 0 && _stats.GetGameLogsForPlayer(playerId).Count == 0)
        {
            return null;
        }

        var nflSeasons = seasons
            .Where(r => r.Level == FootballLevel.Nfl && r.Period != StatsPeriod.College)
            .OrderByDescending(r => r.Season)
            .ToList();
        var college = seasons.Where(r => r.Period == StatsPeriod.College || r.Level == FootballLevel.College)
            .OrderByDescending(r => r.Season)
            .ToList();
        var games = _stats.GetGameLogsForPlayer(playerId)
            .OrderByDescending(g => g.Season)
            .ThenByDescending(g => g.Week)
            .ToList();
        var recentGames = games.Take(8).ToList();

        var career = _stats.GetCareerTotals(playerId);
        var historical = nflSeasons
            .Where(r => r.Period == StatsPeriod.CompletedSeason)
            .Take(3)
            .ToList();
        var current = nflSeasons.FirstOrDefault(r => r.Period == StatsPeriod.CurrentSeason);

        return new PlayerStatisticalContext
        {
            PlayerId = playerId,
            AsOf = DateTimeOffset.UtcNow,
            RecentProduction = BuildWindow(
                FootballLevel.Nfl,
                recentGames.Count > 0 ? "Recent NFL games" : "Current / latest NFL season",
                recentGames.Count > 0
                    ? SumGames(recentGames)
                    : current?.ToCountingStats() ?? nflSeasons.FirstOrDefault()?.ToCountingStats(),
                recentGames.Count > 0 ? recentGames.Count : current?.Games ?? nflSeasons.FirstOrDefault()?.Games),
            HistoricalProduction = historical.Count == 0
                ? null
                : BuildWindow(
                    FootballLevel.Nfl,
                    $"NFL seasons {string.Join("/", historical.Select(h => h.Season))}",
                    SumSeasons(historical),
                    historical.Sum(h => h.Games ?? 0)),
            CareerBaseline = career is null
                ? null
                : BuildWindow(FootballLevel.Career, "NFL career", career.ToCountingStats(), career.Games),
            CollegeProduction = college.Count == 0
                ? null
                : BuildWindow(
                    FootballLevel.College,
                    "College (separate from NFL)",
                    SumSeasons(college),
                    college.Sum(c => c.Games ?? 0)),
            Usage = BuildUsage(recentGames, current ?? nflSeasons.FirstOrDefault()),
            Efficiency = BuildEfficiency(current ?? nflSeasons.FirstOrDefault()),
            Consistency = BuildConsistency(recentGames),
            VolatilityScore = ComputeVolatility(recentGames),
            Trend = ComputeTrend(recentGames),
            GameLogsAvailable = games.Count,
            NflSeasonsAvailable = nflSeasons.Select(s => s.Season).Distinct().Count(),
            HasCollegeStatistics = college.Count > 0,
            PrimarySourceProvider = nflSeasons.FirstOrDefault()?.SourceProvider
                                    ?? college.FirstOrDefault()?.SourceProvider
        };
    }

    public IReadOnlyList<PlayerStatisticalContext> GetAllContexts()
    {
        var ids = _stats.GetAllStats().Select(s => s.PlayerId)
            .Concat(_stats.GetAllGameLogs().Select(g => g.PlayerId))
            .Distinct();
        return ids.Select(GetContext).Where(c => c is not null).Cast<PlayerStatisticalContext>().ToList();
    }

    private static StatisticalProductionWindow BuildWindow(
        FootballLevel level,
        string label,
        CanonicalCountingStats? totals,
        int? games)
    {
        CanonicalCountingStats? perGame = null;
        if (totals is not null && games is > 0)
        {
            perGame = Scale(totals, 1m / games.Value);
        }

        return new StatisticalProductionWindow
        {
            Level = level,
            Label = label,
            Games = games,
            Totals = totals,
            PerGame = perGame
        };
    }

    private static StatisticalUsageSignals? BuildUsage(
        IReadOnlyList<PlayerGameStats> recent,
        PlayerSeasonStats? season)
    {
        if (recent.Count > 0)
        {
            var g = recent.Count;
            var carries = recent.Sum(r => r.RushAttempts ?? 0);
            var targets = recent.Sum(r => r.Targets ?? 0);
            var passAtt = recent.Sum(r => r.PassAttempts ?? 0);
            var rec = recent.Sum(r => r.Receptions ?? 0);
            var firstHalf = recent.Take(recent.Count / 2).Sum(r => (r.RushAttempts ?? 0) + (r.Targets ?? 0));
            var secondHalf = recent.Skip(recent.Count / 2).Sum(r => (r.RushAttempts ?? 0) + (r.Targets ?? 0));
            var workload = secondHalf == firstHalf
                ? "stable"
                : secondHalf > firstHalf
                    ? "increasing"
                    : "decreasing";

            return new StatisticalUsageSignals
            {
                CarriesPerGame = Round(carries / (decimal)g),
                TargetsPerGame = Round(targets / (decimal)g),
                PassAttemptsPerGame = Round(passAtt / (decimal)g),
                ReceptionsPerGame = Round(rec / (decimal)g),
                WorkloadTrend = workload
            };
        }

        if (season?.Games is > 0)
        {
            var g = season.Games.Value;
            return new StatisticalUsageSignals
            {
                CarriesPerGame = Round((season.RushAttempts ?? 0) / (decimal)g),
                TargetsPerGame = Round((season.Targets ?? 0) / (decimal)g),
                PassAttemptsPerGame = Round((season.PassAttempts ?? 0) / (decimal)g),
                ReceptionsPerGame = Round((season.Receptions ?? 0) / (decimal)g),
                WorkloadTrend = "unknown"
            };
        }

        return null;
    }

    private static StatisticalEfficiencySignals? BuildEfficiency(PlayerSeasonStats? season)
    {
        if (season is null)
        {
            return null;
        }

        return new StatisticalEfficiencySignals
        {
            YardsPerCarry = season.RushAttempts is > 0
                ? Round((season.RushYards ?? 0) / (decimal)season.RushAttempts.Value)
                : null,
            YardsPerTarget = season.Targets is > 0
                ? Round((season.ReceivingYards ?? 0) / (decimal)season.Targets.Value)
                : null,
            YardsPerReception = season.Receptions is > 0
                ? Round((season.ReceivingYards ?? 0) / (decimal)season.Receptions.Value)
                : null,
            CompletionPercentage = season.PassAttempts is > 0
                ? Round(100m * (season.PassCompletions ?? 0) / season.PassAttempts.Value)
                : null,
            YardsPerPassAttempt = season.PassAttempts is > 0
                ? Round((season.PassYards ?? 0) / (decimal)season.PassAttempts.Value)
                : null
        };
    }

    private static StatisticalConsistencySignals? BuildConsistency(IReadOnlyList<PlayerGameStats> recent)
    {
        if (recent.Count < 2)
        {
            return null;
        }

        var points = recent
            .Select(g => LeagueFantasyScoring.Calculate(g, Core.Leagues.ScoringType.Ppr))
            .ToList();
        var mean = points.Average();
        var variance = points.Sum(p => (p - mean) * (p - mean)) / points.Count;
        var std = (decimal)Math.Sqrt((double)variance);
        decimal? cov = mean == 0 ? null : Round(std / mean);

        var ordered = points.OrderBy(p => p).ToList();
        var median = ordered[ordered.Count / 2];

        return new StatisticalConsistencySignals
        {
            CoefficientOfVariation = cov,
            GamesWithCountingStats = recent.Count(r => r.HasAnyCountingStat),
            MedianWeeklyFantasyPpr = median
        };
    }

    private static decimal? ComputeVolatility(IReadOnlyList<PlayerGameStats> recent)
    {
        var consistency = BuildConsistency(recent);
        if (consistency?.CoefficientOfVariation is null)
        {
            return null;
        }

        return Math.Clamp(Math.Round(consistency.CoefficientOfVariation.Value * 50m, 1), 0, 100);
    }

    private static StatisticalTrendSignal ComputeTrend(IReadOnlyList<PlayerGameStats> recent)
    {
        if (recent.Count < 4)
        {
            return StatisticalTrendSignal.Unknown;
        }

        // recent is newest-first
        var newer = recent.Take(recent.Count / 2)
            .Select(g => LeagueFantasyScoring.Calculate(g, Core.Leagues.ScoringType.Ppr))
            .Average();
        var older = recent.Skip(recent.Count / 2)
            .Select(g => LeagueFantasyScoring.Calculate(g, Core.Leagues.ScoringType.Ppr))
            .Average();
        var delta = newer - older;
        var consistency = BuildConsistency(recent);
        if (consistency?.CoefficientOfVariation is > 0.55m)
        {
            return StatisticalTrendSignal.Volatile;
        }

        if (Math.Abs(delta) < 1.5m)
        {
            return StatisticalTrendSignal.Stable;
        }

        return delta > 0 ? StatisticalTrendSignal.Increasing : StatisticalTrendSignal.Decreasing;
    }

    private static CanonicalCountingStats SumGames(IReadOnlyList<PlayerGameStats> games) =>
        new()
        {
            PassAttempts = Sum(games.Select(g => g.PassAttempts)),
            PassCompletions = Sum(games.Select(g => g.PassCompletions)),
            PassYards = Sum(games.Select(g => g.PassYards)),
            PassTouchdowns = Sum(games.Select(g => g.PassTouchdowns)),
            PassInterceptions = Sum(games.Select(g => g.PassInterceptions)),
            RushAttempts = Sum(games.Select(g => g.RushAttempts)),
            RushYards = Sum(games.Select(g => g.RushYards)),
            RushTouchdowns = Sum(games.Select(g => g.RushTouchdowns)),
            Targets = Sum(games.Select(g => g.Targets)),
            Receptions = Sum(games.Select(g => g.Receptions)),
            ReceivingYards = Sum(games.Select(g => g.ReceivingYards)),
            ReceivingTouchdowns = Sum(games.Select(g => g.ReceivingTouchdowns)),
            Fumbles = Sum(games.Select(g => g.Fumbles))
        };

    private static CanonicalCountingStats SumSeasons(IReadOnlyList<PlayerSeasonStats> seasons) =>
        new()
        {
            PassAttempts = Sum(seasons.Select(g => g.PassAttempts)),
            PassCompletions = Sum(seasons.Select(g => g.PassCompletions)),
            PassYards = Sum(seasons.Select(g => g.PassYards)),
            PassTouchdowns = Sum(seasons.Select(g => g.PassTouchdowns)),
            PassInterceptions = Sum(seasons.Select(g => g.PassInterceptions)),
            RushAttempts = Sum(seasons.Select(g => g.RushAttempts)),
            RushYards = Sum(seasons.Select(g => g.RushYards)),
            RushTouchdowns = Sum(seasons.Select(g => g.RushTouchdowns)),
            Targets = Sum(seasons.Select(g => g.Targets)),
            Receptions = Sum(seasons.Select(g => g.Receptions)),
            ReceivingYards = Sum(seasons.Select(g => g.ReceivingYards)),
            ReceivingTouchdowns = Sum(seasons.Select(g => g.ReceivingTouchdowns)),
            Fumbles = Sum(seasons.Select(g => g.Fumbles))
        };

    private static int? Sum(IEnumerable<int?> values)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return list.Count == 0 ? null : list.Sum();
    }

    private static CanonicalCountingStats Scale(CanonicalCountingStats s, decimal factor) =>
        new()
        {
            PassAttempts = ScaleInt(s.PassAttempts, factor),
            PassCompletions = ScaleInt(s.PassCompletions, factor),
            PassYards = ScaleInt(s.PassYards, factor),
            PassTouchdowns = ScaleInt(s.PassTouchdowns, factor),
            PassInterceptions = ScaleInt(s.PassInterceptions, factor),
            RushAttempts = ScaleInt(s.RushAttempts, factor),
            RushYards = ScaleInt(s.RushYards, factor),
            RushTouchdowns = ScaleInt(s.RushTouchdowns, factor),
            Targets = ScaleInt(s.Targets, factor),
            Receptions = ScaleInt(s.Receptions, factor),
            ReceivingYards = ScaleInt(s.ReceivingYards, factor),
            ReceivingTouchdowns = ScaleInt(s.ReceivingTouchdowns, factor),
            Fumbles = ScaleInt(s.Fumbles, factor)
        };

    private static int? ScaleInt(int? value, decimal factor) =>
        value is null ? null : (int)Math.Round(value.Value * factor, MidpointRounding.AwayFromZero);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
