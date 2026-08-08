using Playbook.Application.Players;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Projections.Services;

/// <summary>
/// Resolves player-specific production:
/// 1) curated season catalog (by normalized name),
/// 2) non-empty <see cref="SeasonStats"/> from the player profile,
/// 3) documented attribute fallback (position + YearsPro + Age + Status).
/// </summary>
public sealed class PlayerProductionProvider : IPlayerProductionProvider
{
    private readonly IPlayerService _players;
    private readonly IReadOnlyDictionary<string, CuratedSeasonRow> _catalog;

    public PlayerProductionProvider(IPlayerService players)
    {
        _players = players;
        _catalog = BuildCatalog();
    }

    public PlayerProductionSnapshot GetProduction(Player player)
    {
        if (_catalog.TryGetValue(Normalize(player.FullName), out var curated))
        {
            return ToSnapshot(player, curated, ProductionDataSource.CuratedSeason,
                $"Curated {curated.Season} season production for {player.FullName}.");
        }

        var profileStats = _players.GetPlayerProfile(player.Id)?.SeasonStats;
        if (profileStats is not null && HasMeaningfulBoxScore(profileStats))
        {
            // Only use profile stats when they look player-populated (future live stats path).
            // Identical position templates are avoided by preferring curated above; remaining
            // mock templates still flow here only if the name was not curated.
            return FromSeasonStats(player, profileStats, ProductionDataSource.ProfileSeason,
                $"Profile season stats ({profileStats.Season}) for {player.FullName}.");
        }

        return BuildAttributeFallback(player);
    }

    private static PlayerProductionSnapshot BuildAttributeFallback(Player player)
    {
        var years = Math.Clamp(player.YearsPro ?? 0, 0, 20);
        var age = player.Age ?? EstimateAge(player.Position, years);
        var experienceFactor = ExperienceFactor(years);
        var ageFactor = AgeFactor(player.Position, age);
        var statusFactor = StatusFactor(player.Status);

        var (games, row) = PositionFallbackShell(player.Position);
        row = ScaleRow(row, experienceFactor * ageFactor);

        // Status is applied as a production availability factor (Out/IR nearly zeros volume).
        row = ScaleRow(row, statusFactor);

        var description =
            $"Attribute fallback (no live box-score stats): {player.Position}, " +
            $"YearsPro={player.YearsPro?.ToString() ?? "n/a"}, Age={player.Age?.ToString() ?? "n/a"}, " +
            $"Status={player.Status}. Factors exp={experienceFactor:0.00}, age={ageFactor:0.00}, " +
            $"status={statusFactor:0.00}.";

        return ToSnapshot(player, row with { Season = 2025, GamesPlayed = games },
            ProductionDataSource.AttributeFallback, description);
    }

    private static (int Games, CuratedSeasonRow Row) PositionFallbackShell(Position position) =>
        position switch
        {
            Position.QB => (17, new CuratedSeasonRow(2025, 17, 3400, 22, 10, 40, 250, 2, 0, 0, 0, 0, null)),
            Position.RB => (16, new CuratedSeasonRow(2025, 16, 0, 0, 0, 220, 900, 6, 50, 35, 280, 1, null)),
            Position.WR => (16, new CuratedSeasonRow(2025, 16, 0, 0, 0, 5, 30, 0, 120, 75, 950, 6, null)),
            Position.TE => (16, new CuratedSeasonRow(2025, 16, 0, 0, 0, 0, 0, 0, 90, 60, 650, 4, null)),
            Position.K => (17, new CuratedSeasonRow(2025, 17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8.0m)),
            Position.DST => (17, new CuratedSeasonRow(2025, 17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7.5m)),
            _ => (16, new CuratedSeasonRow(2025, 16, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 6.0m))
        };

    private static decimal ExperienceFactor(int yearsPro) => yearsPro switch
    {
        0 => 0.72m,
        1 => 0.82m,
        2 => 0.92m,
        >= 3 and <= 8 => 1.00m,
        >= 9 and <= 12 => 0.94m,
        _ => 0.86m
    };

    private static decimal AgeFactor(Position position, int age)
    {
        if (position is Position.RB && age >= 30)
        {
            return 0.88m;
        }

        if (position is Position.WR or Position.TE && age >= 33)
        {
            return 0.90m;
        }

        if (position is Position.QB && age >= 38)
        {
            return 0.92m;
        }

        return 1.00m;
    }

    private static decimal StatusFactor(PlayerStatus status) => status switch
    {
        PlayerStatus.Out => 0.20m,
        PlayerStatus.InjuredReserve => 0.10m,
        PlayerStatus.Doubtful => 0.55m,
        PlayerStatus.Questionable => 0.85m,
        PlayerStatus.Suspended => 0.30m,
        PlayerStatus.PracticeSquad => 0.45m,
        _ => 1.00m
    };

    private static int EstimateAge(Position position, int yearsPro) =>
        position switch
        {
            Position.QB => 22 + yearsPro,
            Position.RB => 22 + yearsPro,
            _ => 22 + yearsPro
        };

    private static CuratedSeasonRow ScaleRow(CuratedSeasonRow row, decimal factor) =>
        row with
        {
            PassingYards = ScaleInt(row.PassingYards, factor),
            PassingTouchdowns = ScaleInt(row.PassingTouchdowns, factor),
            Interceptions = ScaleInt(row.Interceptions, factor),
            RushingAttempts = ScaleInt(row.RushingAttempts, factor),
            RushingYards = ScaleInt(row.RushingYards, factor),
            RushingTouchdowns = ScaleInt(row.RushingTouchdowns, factor),
            Targets = ScaleInt(row.Targets, factor),
            Receptions = ScaleInt(row.Receptions, factor),
            ReceivingYards = ScaleInt(row.ReceivingYards, factor),
            ReceivingTouchdowns = ScaleInt(row.ReceivingTouchdowns, factor),
            SpecialistWeeklyPrior = row.SpecialistWeeklyPrior is decimal w
                ? Math.Round(w * factor, 1, MidpointRounding.AwayFromZero)
                : null
        };

    private static int ScaleInt(int value, decimal factor) =>
        (int)Math.Round(value * (double)factor, MidpointRounding.AwayFromZero);

    private static bool HasMeaningfulBoxScore(SeasonStats stats) =>
        stats.PassingYards > 0 ||
        stats.RushingYards > 0 ||
        stats.ReceivingYards > 0 ||
        stats.Receptions > 0 ||
        stats.Targets > 0 ||
        stats.FantasyPoints > 0;

    private static PlayerProductionSnapshot FromSeasonStats(
        Player player,
        SeasonStats stats,
        ProductionDataSource source,
        string description)
    {
        decimal? specialist = null;
        if (player.Position is Position.K or Position.DST)
        {
            specialist = stats.GamesPlayed > 0
                ? Math.Round(stats.FantasyPoints / stats.GamesPlayed, 1, MidpointRounding.AwayFromZero)
                : stats.FantasyPoints;
        }

        return new PlayerProductionSnapshot
        {
            PlayerId = player.Id,
            PlayerName = player.FullName,
            Position = player.Position,
            Season = stats.Season,
            Source = source,
            SourceDescription = description,
            GamesPlayed = Math.Max(1, stats.GamesPlayed),
            PassingYards = stats.PassingYards,
            PassingTouchdowns = stats.PassingTouchdowns,
            Interceptions = 0,
            RushingYards = stats.RushingYards,
            RushingTouchdowns = stats.RushingTouchdowns,
            Targets = stats.Targets,
            Receptions = stats.Receptions,
            ReceivingYards = stats.ReceivingYards,
            ReceivingTouchdowns = stats.ReceivingTouchdowns,
            SpecialistWeeklyPrior = specialist
        };
    }

    private static PlayerProductionSnapshot ToSnapshot(
        Player player,
        CuratedSeasonRow row,
        ProductionDataSource source,
        string description) =>
        new()
        {
            PlayerId = player.Id,
            PlayerName = player.FullName,
            Position = player.Position,
            Season = row.Season,
            Source = source,
            SourceDescription = description,
            GamesPlayed = Math.Max(1, row.GamesPlayed),
            PassingYards = row.PassingYards,
            PassingTouchdowns = row.PassingTouchdowns,
            Interceptions = row.Interceptions,
            RushingAttempts = row.RushingAttempts,
            RushingYards = row.RushingYards,
            RushingTouchdowns = row.RushingTouchdowns,
            Targets = row.Targets,
            Receptions = row.Receptions,
            ReceivingYards = row.ReceivingYards,
            ReceivingTouchdowns = row.ReceivingTouchdowns,
            SpecialistWeeklyPrior = row.SpecialistWeeklyPrior
        };

    private static string Normalize(string name) =>
        string.Join(' ', name
            .Trim()
            .ToLowerInvariant()
            .Replace(".", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Approximate 2024 NFL season production for differentiation.
    /// Replaceable by a live stats provider later.
    /// </summary>
    private static IReadOnlyDictionary<string, CuratedSeasonRow> BuildCatalog()
    {
        var rows = new Dictionary<string, CuratedSeasonRow>(StringComparer.OrdinalIgnoreCase)
        {
            // QBs
            ["patrick mahomes"] = new(2024, 16, 3928, 26, 11, 58, 307, 2, 0, 0, 0, 0, null),
            ["josh allen"] = new(2024, 17, 3731, 28, 6, 102, 531, 12, 0, 0, 0, 0, null),
            ["jayden daniels"] = new(2024, 17, 3568, 25, 9, 148, 891, 6, 0, 0, 0, 0, null),
            ["jordan love"] = new(2024, 15, 3389, 25, 11, 25, 83, 1, 0, 0, 0, 0, null),
            ["lamar jackson"] = new(2024, 17, 4172, 41, 4, 139, 915, 4, 0, 0, 0, 0, null),
            ["joe burrow"] = new(2024, 17, 4918, 43, 9, 42, 201, 2, 0, 0, 0, 0, null),
            ["jalen hurts"] = new(2024, 15, 2903, 18, 5, 150, 630, 14, 0, 0, 0, 0, null),
            ["baker mayfield"] = new(2024, 17, 4500, 41, 16, 60, 378, 3, 0, 0, 0, 0, null),

            // RBs
            ["saquon barkley"] = new(2024, 16, 0, 0, 0, 345, 2005, 13, 43, 33, 278, 2, null),
            ["bijan robinson"] = new(2024, 17, 0, 0, 0, 304, 1456, 14, 80, 61, 431, 1, null),
            ["bucky irving"] = new(2024, 17, 0, 0, 0, 207, 1122, 8, 52, 47, 392, 0, null),
            ["jahmyr gibbs"] = new(2024, 17, 0, 0, 0, 250, 1412, 16, 63, 52, 517, 4, null),
            ["derrick henry"] = new(2024, 17, 0, 0, 0, 325, 1921, 16, 19, 19, 193, 2, null),

            // WRs
            ["jamarr chase"] = new(2024, 17, 0, 0, 0, 3, 22, 0, 175, 127, 1708, 17, null),
            ["ceedee lamb"] = new(2024, 15, 0, 0, 0, 10, 70, 0, 130, 101, 1194, 6, null),
            ["amon-ra st brown"] = new(2024, 17, 0, 0, 0, 2, 12, 0, 141, 115, 1263, 12, null),
            ["amonra st brown"] = new(2024, 17, 0, 0, 0, 2, 12, 0, 141, 115, 1263, 12, null),
            ["brian thomas jr"] = new(2024, 17, 0, 0, 0, 4, 28, 0, 133, 87, 1282, 10, null),
            ["puka nacua"] = new(2024, 11, 0, 0, 0, 2, 11, 0, 106, 79, 990, 3, null),
            ["justin jefferson"] = new(2024, 17, 0, 0, 0, 1, 3, 0, 154, 103, 1533, 10, null),
            ["tyreek hill"] = new(2024, 17, 0, 0, 0, 5, 26, 0, 123, 81, 959, 6, null),

            // TEs
            ["travis kelce"] = new(2024, 16, 0, 0, 0, 0, 0, 0, 133, 97, 823, 3, null),
            ["brock bowers"] = new(2024, 17, 0, 0, 0, 2, 13, 0, 153, 112, 1194, 5, null),
            ["trey mcbride"] = new(2024, 16, 0, 0, 0, 0, 0, 0, 147, 111, 1146, 2, null),
            ["george kittle"] = new(2024, 15, 0, 0, 0, 0, 0, 0, 94, 78, 1106, 8, null),

            // K / DST (weekly priors)
            ["justin tucker"] = new(2024, 17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8.4m),
            ["harrison butker"] = new(2024, 17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 9.1m),
            ["buffalo bills"] = new(2024, 17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 9.5m),
            ["san francisco 49ers"] = new(2024, 17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8.2m),
            ["philadelphia eagles"] = new(2024, 17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 9.8m)
        };

        return rows;
    }

    private sealed record CuratedSeasonRow(
        int Season,
        int GamesPlayed,
        int PassingYards,
        int PassingTouchdowns,
        int Interceptions,
        int RushingAttempts,
        int RushingYards,
        int RushingTouchdowns,
        int Targets,
        int Receptions,
        int ReceivingYards,
        int ReceivingTouchdowns,
        decimal? SpecialistWeeklyPrior);
}
