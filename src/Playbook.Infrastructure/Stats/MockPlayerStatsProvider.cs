using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Deterministic multi-season mock stats for selected catalog players.
/// Includes college rows for young players (&lt; 3 NFL seasons). Does not invent stats for unknown players.
/// </summary>
public sealed class MockPlayerStatsProvider : IPlayerStatsProvider
{
    public PlayerStatsProviderKind Kind => PlayerStatsProviderKind.Mock;

    public string DisplayName => "Mock";

    public Task<IReadOnlyList<PlayerSeasonStats>> GetSeasonStatsAsync(
        PlayerStatsSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var rows = new List<PlayerSeasonStats>();

        foreach (var seed in BuildSeeds())
        {
            foreach (var season in request.CompletedSeasons)
            {
                if (seed.Nfl.TryGetValue(season, out var nfl))
                {
                    rows.Add(WithPeriod(nfl, StatsPeriod.CompletedSeason, request.SeasonType, now));
                }
            }

            if (seed.Nfl.TryGetValue(request.CurrentSeason, out var current))
            {
                rows.Add(WithPeriod(current, StatsPeriod.CurrentSeason, request.SeasonType, now));
            }

            if (seed.YearsPro < 3 && seed.College is not null)
            {
                rows.Add(WithPeriod(seed.College, StatsPeriod.College, "college", now));
            }
        }

        return Task.FromResult<IReadOnlyList<PlayerSeasonStats>>(rows);
    }

    private static PlayerSeasonStats WithPeriod(
        PlayerSeasonStats source,
        StatsPeriod period,
        string seasonType,
        DateTimeOffset now) =>
        new()
        {
            PlayerId = source.PlayerId,
            Season = source.Season,
            SeasonType = seasonType,
            Period = period,
            Games = source.Games,
            Starts = source.Starts,
            PassAttempts = source.PassAttempts,
            PassCompletions = source.PassCompletions,
            PassYards = source.PassYards,
            PassTouchdowns = source.PassTouchdowns,
            PassInterceptions = source.PassInterceptions,
            RushAttempts = source.RushAttempts,
            RushYards = source.RushYards,
            RushTouchdowns = source.RushTouchdowns,
            Targets = source.Targets,
            Receptions = source.Receptions,
            ReceivingYards = source.ReceivingYards,
            ReceivingTouchdowns = source.ReceivingTouchdowns,
            FantasyPointsStandard = source.FantasyPointsStandard,
            FantasyPointsHalfPpr = source.FantasyPointsHalfPpr,
            FantasyPointsPpr = source.FantasyPointsPpr,
            CollegeSchool = source.CollegeSchool,
            SourceProvider = "Mock",
            LastUpdated = now
        };

    private static List<Seed> BuildSeeds()
    {
        var seeds = new List<Seed>();

        seeds.Add(new Seed(9, QbMap(
            "11111111-1111-1111-1111-111111111103",
            (2023, 16, 16, 597, 401, 4183, 27, 14, 75, 389, 0),
            (2024, 16, 16, 581, 392, 3928, 26, 11, 58, 307, 2),
            (2025, 14, 14, 502, 330, 3588, 22, 11, 64, 422, 5)), null));

        seeds.Add(new Seed(2, QbMap(
            "11111111-1111-1111-1111-111111111101",
            (2024, 17, 17, 480, 331, 3568, 25, 9, 148, 891, 6),
            (2025, 10, 10, 290, 190, 2200, 14, 5, 80, 480, 4)),
            CollegePass("11111111-1111-1111-1111-111111111101", 2023, "LSU", 12, 12, 350, 236, 3812, 40, 4, 120, 1134, 10)));

        seeds.Add(new Seed(7, RbMap(
            "11111111-1111-1111-1111-111111111106",
            (2023, 14, 14, 247, 962, 6, 60, 41, 280, 4),
            (2024, 16, 16, 345, 2005, 13, 43, 33, 278, 2),
            (2025, 12, 12, 240, 1100, 8, 35, 28, 210, 1)), null));

        seeds.Add(new Seed(3, RbMap(
            "11111111-1111-1111-1111-111111111105",
            (2023, 17, 16, 214, 976, 4, 86, 58, 487, 4),
            (2024, 17, 17, 304, 1456, 14, 80, 61, 431, 1),
            (2025, 11, 11, 180, 820, 7, 45, 35, 280, 1)), null));

        seeds.Add(new Seed(5, RecMap(
            "11111111-1111-1111-1111-111111111109",
            (2023, 16, 16, 145, 100, 1216, 7),
            (2024, 17, 17, 175, 127, 1708, 17),
            (2025, 12, 12, 110, 80, 1050, 9)), null));

        seeds.Add(new Seed(12, RecMap(
            "11111111-1111-1111-1111-111111111113",
            (2023, 15, 15, 121, 93, 984, 5),
            (2024, 16, 16, 133, 97, 823, 3),
            (2025, 11, 11, 80, 60, 620, 3)), null));

        seeds.Add(new Seed(2, RecMap(
            "11111111-1111-1111-1111-111111111108",
            (2024, 17, 16, 133, 87, 1282, 10),
            (2025, 9, 9, 70, 45, 620, 4)),
            CollegeRec("11111111-1111-1111-1111-111111111108", 2023, "LSU", 13, 13, 100, 68, 1177, 17)));

        seeds.Add(new Seed(2, RecMap(
            "11111111-1111-1111-1111-111111111114",
            (2024, 17, 16, 153, 112, 1194, 5),
            (2025, 8, 8, 70, 50, 540, 3)),
            CollegeRec("11111111-1111-1111-1111-111111111114", 2023, "Georgia", 10, 10, 80, 56, 714, 6)));

        return seeds;
    }

    private static Dictionary<int, PlayerSeasonStats> QbMap(
        string id,
        params (int Season, int Gp, int Gs, int Att, int Cmp, int Yds, int Td, int Int, int RuAtt, int RuYd, int RuTd)[] seasons) =>
        seasons.ToDictionary(
            s => s.Season,
            s =>
            {
                var pts = Round(s.Yds / 25m + s.Td * 4m - s.Int * 2m + s.RuYd / 10m + s.RuTd * 6m);
                return new PlayerSeasonStats
                {
                    PlayerId = Guid.Parse(id),
                    Season = s.Season,
                    SeasonType = "regular",
                    Period = StatsPeriod.CompletedSeason,
                    Games = s.Gp,
                    Starts = s.Gs,
                    PassAttempts = s.Att,
                    PassCompletions = s.Cmp,
                    PassYards = s.Yds,
                    PassTouchdowns = s.Td,
                    PassInterceptions = s.Int,
                    RushAttempts = s.RuAtt,
                    RushYards = s.RuYd,
                    RushTouchdowns = s.RuTd,
                    FantasyPointsStandard = pts,
                    FantasyPointsHalfPpr = pts,
                    FantasyPointsPpr = pts,
                    SourceProvider = "Mock",
                    LastUpdated = DateTimeOffset.UtcNow
                };
            });

    private static Dictionary<int, PlayerSeasonStats> RbMap(
        string id,
        params (int Season, int Gp, int Gs, int Att, int Yds, int Td, int Tgt, int Rec, int RecYd, int RecTd)[] seasons) =>
        seasons.ToDictionary(
            s => s.Season,
            s =>
            {
                var std = s.Yds / 10m + s.Td * 6m + s.RecYd / 10m + s.RecTd * 6m;
                return new PlayerSeasonStats
                {
                    PlayerId = Guid.Parse(id),
                    Season = s.Season,
                    SeasonType = "regular",
                    Period = StatsPeriod.CompletedSeason,
                    Games = s.Gp,
                    Starts = s.Gs,
                    RushAttempts = s.Att,
                    RushYards = s.Yds,
                    RushTouchdowns = s.Td,
                    Targets = s.Tgt,
                    Receptions = s.Rec,
                    ReceivingYards = s.RecYd,
                    ReceivingTouchdowns = s.RecTd,
                    FantasyPointsStandard = Round(std),
                    FantasyPointsHalfPpr = Round(std + s.Rec * 0.5m),
                    FantasyPointsPpr = Round(std + s.Rec),
                    SourceProvider = "Mock",
                    LastUpdated = DateTimeOffset.UtcNow
                };
            });

    private static Dictionary<int, PlayerSeasonStats> RecMap(
        string id,
        params (int Season, int Gp, int Gs, int Tgt, int Rec, int Yds, int Td)[] seasons) =>
        seasons.ToDictionary(
            s => s.Season,
            s =>
            {
                var std = s.Yds / 10m + s.Td * 6m;
                return new PlayerSeasonStats
                {
                    PlayerId = Guid.Parse(id),
                    Season = s.Season,
                    SeasonType = "regular",
                    Period = StatsPeriod.CompletedSeason,
                    Games = s.Gp,
                    Starts = s.Gs,
                    Targets = s.Tgt,
                    Receptions = s.Rec,
                    ReceivingYards = s.Yds,
                    ReceivingTouchdowns = s.Td,
                    FantasyPointsStandard = Round(std),
                    FantasyPointsHalfPpr = Round(std + s.Rec * 0.5m),
                    FantasyPointsPpr = Round(std + s.Rec),
                    SourceProvider = "Mock",
                    LastUpdated = DateTimeOffset.UtcNow
                };
            });

    private static PlayerSeasonStats CollegePass(
        string id, int season, string school, int gp, int gs,
        int att, int cmp, int yds, int td, int interceptions, int ruAtt, int ruYd, int ruTd)
    {
        var pts = Round(yds / 25m + td * 4m - interceptions * 2m + ruYd / 10m + ruTd * 6m);
        return new PlayerSeasonStats
        {
            PlayerId = Guid.Parse(id),
            Season = season,
            SeasonType = "college",
            Period = StatsPeriod.College,
            Games = gp,
            Starts = gs,
            PassAttempts = att,
            PassCompletions = cmp,
            PassYards = yds,
            PassTouchdowns = td,
            PassInterceptions = interceptions,
            RushAttempts = ruAtt,
            RushYards = ruYd,
            RushTouchdowns = ruTd,
            FantasyPointsStandard = pts,
            FantasyPointsHalfPpr = pts,
            FantasyPointsPpr = pts,
            CollegeSchool = school,
            SourceProvider = "Mock",
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    private static PlayerSeasonStats CollegeRec(
        string id, int season, string school, int gp, int gs, int tgt, int rec, int yds, int td)
    {
        var std = yds / 10m + td * 6m;
        return new PlayerSeasonStats
        {
            PlayerId = Guid.Parse(id),
            Season = season,
            SeasonType = "college",
            Period = StatsPeriod.College,
            Games = gp,
            Starts = gs,
            Targets = tgt,
            Receptions = rec,
            ReceivingYards = yds,
            ReceivingTouchdowns = td,
            FantasyPointsStandard = Round(std),
            FantasyPointsHalfPpr = Round(std + rec * 0.5m),
            FantasyPointsPpr = Round(std + rec),
            CollegeSchool = school,
            SourceProvider = "Mock",
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private sealed record Seed(
        int YearsPro,
        Dictionary<int, PlayerSeasonStats> Nfl,
        PlayerSeasonStats? College);
}
