using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Deterministic college season stats for young mock catalog players.
/// Does not invent stats for candidates that are not seeded.
/// </summary>
public sealed class MockCollegeStatsProvider : ICollegeStatsProvider
{
    public CollegeStatsProviderKind Kind => CollegeStatsProviderKind.Mock;

    public string DisplayName => "Mock";

    public Task<IReadOnlyList<PlayerSeasonStats>> GetCollegeStatsAsync(
        CollegeStatsSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seeds = BuildSeeds().ToDictionary(s => s.PlayerId);
        var rows = new List<PlayerSeasonStats>();

        foreach (var candidate in request.Candidates)
        {
            if ((candidate.YearsPro ?? 0) >= 3)
            {
                continue;
            }

            if (!seeds.TryGetValue(candidate.PlayerId, out var seed))
            {
                continue;
            }

            rows.AddRange(seed.Seasons);
        }

        // When no candidates are supplied (unit helpers), return all seeds.
        if (request.Candidates.Count == 0)
        {
            rows.AddRange(BuildSeeds().SelectMany(s => s.Seasons));
        }

        return Task.FromResult<IReadOnlyList<PlayerSeasonStats>>(rows);
    }

    private static List<CollegeSeed> BuildSeeds() =>
    [
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111101"),
            [
                CollegePass("11111111-1111-1111-1111-111111111101", 2023, "LSU", 12, 12, 350, 236, 3812, 40, 4, 120, 1134, 10)
            ]),
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111108"),
            [
                CollegeRec("11111111-1111-1111-1111-111111111108", 2023, "LSU", 13, 13, 100, 68, 1177, 17)
            ]),
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111114"),
            [
                CollegeRec("11111111-1111-1111-1111-111111111114", 2023, "Georgia", 10, 10, 80, 56, 714, 6)
            ])
    ];

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
            Level = FootballLevel.College,
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
            Source = "mock-college",
            IdentityMatch = StatsIdentityMatch.Matched,
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
            Level = FootballLevel.College,
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
            Source = "mock-college",
            IdentityMatch = StatsIdentityMatch.Matched,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private sealed record CollegeSeed(Guid PlayerId, IReadOnlyList<PlayerSeasonStats> Seasons);
}
