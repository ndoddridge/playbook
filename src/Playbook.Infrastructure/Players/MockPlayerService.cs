using Playbook.Application.Players;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Players;

/// <summary>
/// In-memory mock player catalog and profiles. Replace with Data Engine later.
/// </summary>
public sealed class MockPlayerService : IPlayerService
{
    private readonly IReadOnlyList<Player> _players;
    private readonly IReadOnlyDictionary<Guid, PlayerProfile> _profiles;

    public MockPlayerService()
    {
        _players = CreatePlayers();
        _profiles = _players.ToDictionary(p => p.Id, CreateProfile);
    }

    public IReadOnlyList<Player> GetAllPlayers() =>
        _players.OrderBy(p => p.Position).ThenBy(p => p.LastName).ToList();

    public Player? GetPlayer(Guid playerId) =>
        _players.FirstOrDefault(p => p.Id == playerId);

    public PlayerProfile? GetPlayerProfile(Guid playerId) =>
        _profiles.TryGetValue(playerId, out var profile) ? profile : null;

    public IReadOnlyList<Player> SearchPlayers(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetAllPlayers();
        }

        var term = query.Trim();
        return GetAllPlayers()
            .Where(p =>
                p.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Team.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Position.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (p.College?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    private static IReadOnlyList<Player> CreatePlayers() =>
    [
        P("11111111-1111-1111-1111-111111111101", "Jayden", "Daniels", Position.QB, "WAS", 5, 24, 2, "LSU", "6'4\"", 210, PlayerStatus.Active, 14),
        P("11111111-1111-1111-1111-111111111102", "Jordan", "Love", Position.QB, "GB", 10, 26, 5, "Utah State", "6'4\"", 219, PlayerStatus.Active, 10),
        P("11111111-1111-1111-1111-111111111103", "Patrick", "Mahomes", Position.QB, "KC", 15, 29, 9, "Texas Tech", "6'2\"", 225, PlayerStatus.Active, 6),
        P("11111111-1111-1111-1111-111111111104", "Bucky", "Irving", Position.RB, "TB", 7, 22, 2, "Oregon", "5'10\"", 195, PlayerStatus.Active, 11),
        P("11111111-1111-1111-1111-111111111105", "Bijan", "Robinson", Position.RB, "ATL", 7, 23, 3, "Texas", "5'11\"", 215, PlayerStatus.Active, 12),
        P("11111111-1111-1111-1111-111111111106", "Saquon", "Barkley", Position.RB, "PHI", 26, 28, 7, "Penn State", "6'0\"", 233, PlayerStatus.Active, 9),
        P("11111111-1111-1111-1111-111111111107", "Jahmyr", "Gibbs", Position.RB, "DET", 0, 23, 3, "Alabama", "5'9\"", 200, PlayerStatus.Active, 5),
        P("11111111-1111-1111-1111-111111111108", "Brian", "Thomas Jr.", Position.WR, "JAX", 7, 23, 2, "LSU", "6'3\"", 209, PlayerStatus.Active, 12),
        P("11111111-1111-1111-1111-111111111109", "Ja'Marr", "Chase", Position.WR, "CIN", 1, 25, 5, "LSU", "6'0\"", 201, PlayerStatus.Active, 12),
        P("11111111-1111-1111-1111-111111111110", "CeeDee", "Lamb", Position.WR, "DAL", 88, 26, 5, "Oklahoma", "6'2\"", 200, PlayerStatus.Questionable, 7),
        P("11111111-1111-1111-1111-111111111111", "Amon-Ra", "St. Brown", Position.WR, "DET", 14, 25, 5, "USC", "6'0\"", 202, PlayerStatus.Active, 5),
        P("11111111-1111-1111-1111-111111111112", "Puka", "Nacua", Position.WR, "LAR", 17, 24, 3, "BYU", "6'2\"", 206, PlayerStatus.Active, 8),
        P("11111111-1111-1111-1111-111111111113", "Travis", "Kelce", Position.TE, "KC", 87, 35, 12, "Cincinnati", "6'5\"", 250, PlayerStatus.Active, 6),
        P("11111111-1111-1111-1111-111111111114", "Brock", "Bowers", Position.TE, "LV", 89, 22, 2, "Georgia", "6'4\"", 230, PlayerStatus.Active, 8),
        P("11111111-1111-1111-1111-111111111115", "Trey", "McBride", Position.TE, "ARI", 85, 25, 4, "Colorado State", "6'4\"", 246, PlayerStatus.Active, 8),
        P("11111111-1111-1111-1111-111111111116", "Justin", "Tucker", Position.K, "BAL", 9, 35, 13, "Texas", "6'1\"", 191, PlayerStatus.Active, 14),
        P("11111111-1111-1111-1111-111111111117", "Harrison", "Butker", Position.K, "KC", 7, 29, 8, "Georgia Tech", "6'4\"", 205, PlayerStatus.Active, 6),
        P("11111111-1111-1111-1111-111111111118", "Buffalo", "Bills", Position.DST, "BUF", null, null, null, null, null, null, PlayerStatus.Active, 12),
        P("11111111-1111-1111-1111-111111111119", "San Francisco", "49ers", Position.DST, "SF", null, null, null, null, null, null, PlayerStatus.Active, 9),
        P("11111111-1111-1111-1111-111111111120", "Philadelphia", "Eagles", Position.DST, "PHI", null, null, null, null, null, null, PlayerStatus.Active, 9)
    ];

    private static Player P(
        string id,
        string first,
        string last,
        Position position,
        string team,
        int? jersey,
        int? age,
        int? yearsPro,
        string? college,
        string? height,
        int? weight,
        PlayerStatus status,
        int? bye) =>
        new()
        {
            Id = Guid.Parse(id),
            FirstName = first,
            LastName = last,
            FullName = $"{first} {last}",
            Position = position,
            Team = team,
            JerseyNumber = jersey,
            Age = age,
            YearsPro = yearsPro,
            College = college,
            Height = height,
            Weight = weight,
            HeadshotUrl = null,
            Status = status,
            ByeWeek = bye
        };

    private static PlayerProfile CreateProfile(Player player)
    {
        var season = player.Position switch
        {
            Position.QB => new SeasonStats
            {
                Season = 2025,
                GamesPlayed = 17,
                FantasyPoints = 340m,
                PassingYards = 4100,
                PassingTouchdowns = 30,
                RushingYards = 550,
                RushingTouchdowns = 5
            },
            Position.RB => new SeasonStats
            {
                Season = 2025,
                GamesPlayed = 16,
                FantasyPoints = 260m,
                RushingYards = 1200,
                RushingTouchdowns = 10,
                Receptions = 45,
                ReceivingYards = 380,
                ReceivingTouchdowns = 2,
                Targets = 60
            },
            Position.WR => new SeasonStats
            {
                Season = 2025,
                GamesPlayed = 16,
                FantasyPoints = 240m,
                Receptions = 95,
                ReceivingYards = 1250,
                ReceivingTouchdowns = 9,
                Targets = 145
            },
            Position.TE => new SeasonStats
            {
                Season = 2025,
                GamesPlayed = 16,
                FantasyPoints = 190m,
                Receptions = 80,
                ReceivingYards = 950,
                ReceivingTouchdowns = 7,
                Targets = 115
            },
            Position.K => new SeasonStats
            {
                Season = 2025,
                GamesPlayed = 17,
                FantasyPoints = 145m
            },
            Position.DST => new SeasonStats
            {
                Season = 2025,
                GamesPlayed = 17,
                FantasyPoints = 130m
            },
            _ => new SeasonStats { Season = 2025 }
        };

        var injuries = player.Status == PlayerStatus.Questionable
            ? new List<InjuryRecord>
            {
                new()
                {
                    ReportedOn = new DateOnly(2026, 8, 5),
                    Description = "Limited in practice — ankle",
                    Status = InjuryStatus.Questionable,
                    BodyPart = "Ankle",
                    ExpectedReturn = new DateOnly(2026, 8, 10)
                }
            }
            : new List<InjuryRecord>();

        return new PlayerProfile
        {
            Player = player,
            SeasonStats = season,
            CareerStats = new CareerStats
            {
                Seasons = player.YearsPro ?? 1,
                GamesPlayed = (player.YearsPro ?? 1) * 15,
                FantasyPoints = season.FantasyPoints * Math.Max(1, player.YearsPro ?? 1) * 0.85m,
                PassingYards = season.PassingYards * Math.Max(1, (player.YearsPro ?? 1) / 2),
                RushingYards = season.RushingYards * Math.Max(1, (player.YearsPro ?? 1) / 2),
                ReceivingYards = season.ReceivingYards * Math.Max(1, (player.YearsPro ?? 1) / 2),
                TotalTouchdowns = season.PassingTouchdowns + season.RushingTouchdowns + season.ReceivingTouchdowns
            },
            CollegeStats = player.College is null
                ? null
                : new CollegeStats
                {
                    School = player.College,
                    Seasons = 3,
                    GamesPlayed = 36,
                    NotableNote = $"Mock college production at {player.College}."
                },
            InjuryHistory = injuries,
            Trend = new PlayerTrend
            {
                Direction = player.Position is Position.RB or Position.WR or Position.QB
                    ? TrendDirection.Up
                    : TrendDirection.Flat,
                Label = player.Position is Position.RB or Position.WR or Position.QB ? "Usage rising" : "Steady role",
                Detail = "Mock trend signal for explorer scaffolding."
            }
        };
    }
}
