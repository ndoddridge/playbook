using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Players;
using Playbook.Core.Stats.Models;
using Playbook.Infrastructure.Players;
using Playbook.Infrastructure.Stats;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// Projections lean on a player's recent games. Those came only from completed seasons, through an
/// nflverse release that stopped after 2024, so "recent form" was two seasons old. These pin down
/// the current release, the season in progress and the recent-form window.
/// </summary>
public class CurrentSeasonStatsTests
{
    // Seasons far from real ones, so the shared on-disk cache never holds a real file for them.
    private const int Completed = 2091;
    private const int Live = 2092;
    private const string Gsis = "00-0099999";

    private static readonly Guid PlayerId = Guid.NewGuid();

    // The current release's columns: team (not recent_team) and passing_interceptions (not interceptions).
    private static string Csv(int season, params (int Week, int PassYards, int Ints)[] weeks)
    {
        var csv = new StringBuilder(
            "player_id,player_name,player_display_name,position,season,week,season_type,team,opponent_team," +
            "completions,attempts,passing_yards,passing_tds,passing_interceptions,carries,rushing_yards,rushing_tds," +
            "receptions,targets,receiving_yards,receiving_tds\n");
        foreach (var (week, yards, ints) in weeks)
        {
            csv.Append($"{Gsis},T.Tester,Test Tester,QB,{season},{week},REG,MIN,NO,20,30,{yards},2,{ints},3,10,0,0,0,0,0\n");
        }

        return csv.ToString();
    }

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private static (NflversePlayerStatsProvider Provider, List<string> Requested) CreateProvider(
        Func<string, HttpResponseMessage> respond)
    {
        var identities = new PlayerIdentityDirectory();
        identities.Upsert(new PlaybookPlayerIdentity { PlaybookId = PlayerId, FullName = "Test Tester", GsisId = Gsis });
        var requested = new List<string>();
        var provider = new NflversePlayerStatsProvider(
            new Factory(new Handler(url =>
            {
                requested.Add(url);
                return respond(url);
            })),
            identities,
            Options.Create(new PlayerStatsOptions()),
            NullLogger<NflversePlayerStatsProvider>.Instance);
        return (provider, requested);
    }

    private static HttpResponseMessage File(string csv) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(Gzip(csv)) };

    private static void ClearCache()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "data", "nflverse-player-stats");
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(dir, $"stats_player_week_{Completed}*")
                     .Concat(Directory.EnumerateFiles(dir, $"stats_player_week_{Live}*")))
        {
            System.IO.File.Delete(file);
        }
    }

    [Fact]
    public async Task Reads_The_Current_Release_And_The_Season_In_Progress()
    {
        ClearCache();
        var (provider, requested) = CreateProvider(url => url.Contains($"_{Live}.")
            ? File(Csv(Live, (1, 250, 1), (2, 210, 0)))
            : File(Csv(Completed, (1, 300, 2), (2, 280, 0), (3, 260, 1))));

        var batch = await provider.GetHistoricalStatsAsync(new HistoricalPlayerStatsSyncRequest
        {
            Seasons = [Live, Completed],
            SeasonType = "regular",
            LiveSeason = Live,
        });

        Assert.All(requested, url => Assert.Contains("/stats_player/stats_player_week_", url));
        Assert.Equal(5, batch.GameLogs.Count);
        Assert.Equal([1, 2], batch.GameLogs.Where(g => g.Season == Live).Select(g => g.Week).Order());
        Assert.All(batch.GameLogs, g => Assert.Equal("MIN", g.Team));
        Assert.Equal(3, batch.GameLogs.Where(g => g.Season == Completed).Sum(g => g.PassInterceptions));

        // The season in progress gives game logs only: its totals come from the season provider.
        var season = Assert.Single(batch.SeasonRecords);
        Assert.Equal((Completed, 3), (season.Season, season.Games));
        ClearCache();
    }

    [Fact]
    public async Task The_Season_In_Progress_Is_Downloaded_Again_Once_Stale_And_Kept_If_That_Fails()
    {
        ClearCache();
        var week = 1;
        var fail = false;
        var (provider, requested) = CreateProvider(_ => fail
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : File(Csv(Live, Enumerable.Range(1, week).Select(w => (w, 200 + w, 0)).ToArray())));
        HistoricalPlayerStatsSyncRequest Request(TimeSpan maxAge) => new()
        {
            Seasons = [Live],
            SeasonType = "regular",
            LiveSeason = Live,
            LiveSeasonMaxAge = maxAge,
        };

        Assert.Single((await provider.GetHistoricalStatsAsync(Request(TimeSpan.FromHours(6)))).GameLogs);

        // Fresh: the saved copy is used.
        week = 2;
        Assert.Single((await provider.GetHistoricalStatsAsync(Request(TimeSpan.FromHours(6)))).GameLogs);
        Assert.Single(requested);

        // Stale: downloaded again, now with week 2.
        Assert.Equal(2, (await provider.GetHistoricalStatsAsync(Request(TimeSpan.Zero))).GameLogs.Count);
        Assert.Equal(2, requested.Count);

        // A failed refresh keeps the saved copy.
        fail = true;
        Assert.Equal(2, (await provider.GetHistoricalStatsAsync(Request(TimeSpan.Zero))).GameLogs.Count);
        ClearCache();
    }

    [Fact]
    public void Recent_Form_Is_This_Season_Once_There_Are_A_Few_Games()
    {
        static PlayerGameStats Game(int season, int week, int yards) => new()
        {
            PlayerId = PlayerId, Season = season, Week = week, SeasonType = "regular", PassYards = yards,
        };
        var lastSeason = Enumerable.Range(10, 8).Select(w => Game(2025, w, 300)).ToList();
        PlayerSeasonStats Current(int games) => new()
        {
            PlayerId = PlayerId, Season = 2026, SeasonType = "regular", Period = StatsPeriod.CurrentSeason,
            Level = FootballLevel.Nfl, Games = games, PassYards = 150 * games,
        };

        // Three games this season: recent form is those three, not last season's.
        var three = new PlayerStatisticalContextService(new Stats(
            [Current(3)], [.. lastSeason, Game(2026, 1, 150), Game(2026, 2, 150), Game(2026, 3, 150)]));
        var recent = three.GetContext(PlayerId)!.RecentProduction!;
        Assert.Equal(3, recent.Games);
        Assert.Equal(150, recent.PerGame!.PassYards);

        // Two games: too few on their own, so the latest games of any season.
        var two = new PlayerStatisticalContextService(new Stats(
            [Current(2)], [.. lastSeason, Game(2026, 1, 150), Game(2026, 2, 150)]));
        Assert.Equal(8, two.GetContext(PlayerId)!.RecentProduction!.Games);
    }

    private sealed class Stats(IReadOnlyList<PlayerSeasonStats> seasons, IReadOnlyList<PlayerGameStats> games)
        : IPlayerStatsService
    {
        public int GameLogCount => games.Count;
        public IReadOnlyList<PlayerSeasonStats> GetAllStats() => seasons;
        public IReadOnlyList<PlayerGameStats> GetAllGameLogs() => games;
        public IReadOnlyList<PlayerSeasonStats> GetStatsForPlayer(Guid playerId) => seasons;
        public IReadOnlyList<PlayerGameStats> GetGameLogsForPlayer(Guid playerId) => games;
        public IReadOnlyList<PlayerGameStats> GetRecentGameLogs(Guid playerId, int maxGames = 8) => games.Take(maxGames).ToList();
        public IReadOnlyList<int> GetAvailableSeasons(Guid playerId) => seasons.Select(s => s.Season).ToList();
        public PlayerSeasonStats? GetStats(Guid playerId, int season, StatsPeriod? period = null) => null;
        public PlayerSeasonStats? GetCareerTotals(Guid playerId) => null;
        public PlayerSeasonStats? GetPrimaryProductionSeason(Guid playerId) => seasons.FirstOrDefault();
        public IReadOnlyList<PlayerSeasonStats> GetRecentNflSeasons(Guid playerId, int maxSeasons = 3) => seasons;
        public void Refresh() { }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshCurrentSeasonAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Handler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request.RequestUri!.ToString()));
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
