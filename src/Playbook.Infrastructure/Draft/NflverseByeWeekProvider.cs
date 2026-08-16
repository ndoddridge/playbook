using Microsoft.Extensions.Logging;
using Playbook.Application.Draft;
using Playbook.Core.Draft;
using Playbook.Infrastructure.Replay.Nflverse;

namespace Playbook.Infrastructure.Draft;

/// <summary>
/// Bye weeks from the nflverse schedules release — the same games.csv the score provider already
/// downloads. Unlike scores, this reads the FULL fixture list including unplayed games, which is
/// how byes are known before a season starts.
///
/// Reads only season/week/teams/game_type. The file's spread_line, total_line and odds columns
/// are never touched.
/// </summary>
public sealed class NflverseByeWeekProvider : IByeWeekProvider
{
    private readonly NflverseCsvCache _cache;
    private readonly ILogger<NflverseByeWeekProvider> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<int, ByeWeekMap> _bySeason = [];

    public NflverseByeWeekProvider(NflverseCsvCache cache, ILogger<NflverseByeWeekProvider> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public ByeWeekMap GetByeWeeks(int season)
    {
        lock (_gate)
        {
            return _bySeason.TryGetValue(season, out var map) ? map : ByeWeekMap.Empty;
        }
    }

    public async Task RefreshAsync(int season, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = await _cache
                .EnsureFileAsync(NflverseReleaseCatalog.SchedulesUrl, "games.csv", cancellationToken)
                .ConfigureAwait(false);

            var lines = await _cache.GetLinesAsync(path, cancellationToken).ConfigureAwait(false);
            var map = ByeWeekMap.Build(ParseSchedule(lines, season));

            lock (_gate)
            {
                _bySeason[season] = map;
            }

            _logger.LogInformation(
                "Bye weeks for {Season}: {Teams} teams covered (available: {Available})",
                season, map.TeamsCovered, map.IsAvailable);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bye-week load failed for {Season}; factor stays inactive", season);
        }
    }

    internal static IReadOnlyList<ScheduledGame> ParseSchedule(IReadOnlyList<string> lines, int season)
    {
        if (lines.Count < 2)
        {
            return [];
        }

        var header = lines[0].Split(',');
        int Idx(string n) => Array.FindIndex(header, h =>
            string.Equals(h.Trim(), n, StringComparison.OrdinalIgnoreCase));

        var iSeason = Idx("season");
        var iWeek = Idx("week");
        var iType = Idx("game_type");
        var iHome = Idx("home_team");
        var iAway = Idx("away_team");

        if (iSeason < 0 || iWeek < 0 || iHome < 0 || iAway < 0)
        {
            return [];
        }

        var games = new List<ScheduledGame>();

        for (var i = 1; i < lines.Count; i++)
        {
            var row = lines[i].Split(',');
            if (row.Length <= Math.Max(iHome, iAway))
            {
                continue;
            }

            if (iType >= 0 && !string.Equals(row[iType], "REG", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(row[iSeason], out var s) || s != season)
            {
                continue;
            }

            if (!int.TryParse(row[iWeek], out var week))
            {
                continue;
            }

            games.Add(new ScheduledGame
            {
                Season = s,
                Week = week,
                HomeTeam = row[iHome].Trim(),
                AwayTeam = row[iAway].Trim()
            });
        }

        return games;
    }
}
