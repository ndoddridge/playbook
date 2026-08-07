using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Players;

/// <summary>
/// Player Engine facade. Loads from the configured <see cref="IPlayerDataProvider"/> and
/// automatically falls back to <see cref="MockPlayerDataProvider"/> when live data fails.
/// UI continues to consume only <see cref="IPlayerService"/>.
/// </summary>
public sealed class PlayerService : IPlayerService
{
    private readonly IPlayerDataProvider _primary;
    private readonly MockPlayerDataProvider _fallback;
    private readonly PlayerDataSyncStatus _status;
    private readonly ILogger<PlayerService> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<Player> _players = [];
    private IReadOnlyDictionary<Guid, PlayerProfile> _profiles =
        new Dictionary<Guid, PlayerProfile>();
    private bool _loaded;

    public PlayerService(
        IEnumerable<IPlayerDataProvider> providers,
        MockPlayerDataProvider fallback,
        PlayerDataSyncStatus status,
        IOptions<PlayerDataOptions> options,
        ILogger<PlayerService> logger)
    {
        _fallback = fallback;
        _status = status;
        _logger = logger;

        var configured = options.Value.Provider;
        _status.SetConfigured(configured);

        _primary = providers.FirstOrDefault(p => p.Kind == configured)
                   ?? fallback;

        if (_primary.Kind != configured)
        {
            _logger.LogWarning(
                "Configured provider {Configured} was not registered; using {Actual}",
                configured,
                _primary.Kind);
        }
    }

    public IReadOnlyList<Player> GetAllPlayers()
    {
        EnsureLoaded();
        return _players;
    }

    public Player? GetPlayer(Guid playerId)
    {
        EnsureLoaded();
        return _players.FirstOrDefault(p => p.Id == playerId);
    }

    public PlayerProfile? GetPlayerProfile(Guid playerId)
    {
        EnsureLoaded();
        return _profiles.TryGetValue(playerId, out var profile) ? profile : null;
    }

    public IReadOnlyList<Player> SearchPlayers(string? query)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(query))
        {
            return _players;
        }

        var term = query.Trim();
        return _players
            .Where(p =>
                p.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Team.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Position.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (p.College?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    public void Refresh()
    {
        lock (_gate)
        {
            LoadCatalog();
            _loaded = true;
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            LoadCatalog();
            _loaded = true;
        }
    }

    private void LoadCatalog()
    {
        var stopwatch = Stopwatch.StartNew();
        string? error = null;

        try
        {
            var players = _primary.GetPlayersAsync().GetAwaiter().GetResult();
            stopwatch.Stop();

            if (players.Count == 0)
            {
                throw new InvalidOperationException($"{_primary.DisplayName} returned an empty catalog.");
            }

            ApplyCatalog(players, enrichMockProfiles: _primary.Kind == PlayerDataProviderKind.Mock);
            _status.RecordSuccess(
                _primary.Kind,
                players.Count,
                stopwatch.Elapsed,
                usedFallback: false,
                priorError: null);

            _logger.LogInformation(
                "Player catalog loaded from {Provider} ({Count} players, {ElapsedMs} ms)",
                _primary.DisplayName,
                players.Count,
                stopwatch.ElapsedMilliseconds);
            return;
        }
        catch (Exception ex) when (_primary.Kind != PlayerDataProviderKind.Mock)
        {
            stopwatch.Stop();
            error = $"{_primary.DisplayName} failed: {ex.Message}";
            _status.RecordFailure(error);
            _logger.LogWarning(ex, "Live player provider failed; falling back to mock catalog");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            error = $"{_primary.DisplayName} failed: {ex.Message}";
            _status.RecordFailure(error);
            _logger.LogError(ex, "Mock player provider failed unexpectedly");
            throw;
        }

        // Fallback path — never leave the app without players when live fails.
        var fallbackWatch = Stopwatch.StartNew();
        var mockPlayers = _fallback.GetPlayersAsync().GetAwaiter().GetResult();
        fallbackWatch.Stop();

        ApplyCatalog(mockPlayers, enrichMockProfiles: true);
        _status.RecordSuccess(
            PlayerDataProviderKind.Mock,
            mockPlayers.Count,
            fallbackWatch.Elapsed,
            usedFallback: true,
            priorError: error);

        _logger.LogInformation(
            "Player catalog served from mock fallback ({Count} players)",
            mockPlayers.Count);
    }

    private void ApplyCatalog(IReadOnlyList<Player> players, bool enrichMockProfiles)
    {
        _players = players
            .OrderBy(p => p.Position)
            .ThenBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ToList();

        _profiles = _players.ToDictionary(
            p => p.Id,
            p => enrichMockProfiles ? CreateMockProfile(p) : CreateLiveProfile(p));
    }

    private static PlayerProfile CreateLiveProfile(Player player) =>
        new()
        {
            Player = player,
            SeasonStats = null,
            CareerStats = null,
            CollegeStats = player.College is null
                ? null
                : new CollegeStats
                {
                    School = player.College,
                    Seasons = 0,
                    GamesPlayed = 0,
                    NotableNote = "College detail not supplied by the live player provider yet."
                },
            InjuryHistory = [],
            Trend = null
        };

    private static PlayerProfile CreateMockProfile(Player player)
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
