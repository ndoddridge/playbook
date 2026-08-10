using System.Globalization;
using Playbook.Application.Replay;

namespace Playbook.Infrastructure.Replay.Nflverse;

/// <summary>Resolves REG week bounds from nflverse schedules.</summary>
public sealed class NflverseHistoricalSeasonCalendar : IHistoricalSeasonCalendar
{
    private readonly NflverseCsvCache _cache;

    public NflverseHistoricalSeasonCalendar(NflverseCsvCache cache)
    {
        _cache = cache;
    }

    public async Task<int> GetRegularSeasonEndWeekAsync(int season, CancellationToken cancellationToken = default)
    {
        var path = await _cache.EnsureFileAsync(
                NflverseReleaseCatalog.SchedulesUrl,
                "games.csv",
                cancellationToken)
            .ConfigureAwait(false);
        var lines = await _cache.GetLinesAsync(path, cancellationToken).ConfigureAwait(false);
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Schedules CSV missing header.");
        }

        var header = SplitCsv(lines[0]);
        var idx = Index(header);
        var maxWeek = 0;
        var seasonText = season.ToString(CultureInfo.InvariantCulture);

        for (var i = 1; i < lines.Count; i++)
        {
            var row = SplitCsv(lines[i]);
            if (Get(row, idx, "season") != seasonText)
            {
                continue;
            }

            var gameType = Get(row, idx, "game_type");
            if (!string.IsNullOrEmpty(gameType) &&
                !string.Equals(gameType, "REG", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(Get(row, idx, "week"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var week))
            {
                continue;
            }

            if (week > maxWeek)
            {
                maxWeek = week;
            }
        }

        if (maxWeek < 1)
        {
            throw new InvalidOperationException($"No REG schedule weeks found for season {season}.");
        }

        return maxWeek;
    }

    private static Dictionary<string, int> Index(IReadOnlyList<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            map[header[i]] = i;
        }

        return map;
    }

    private static string Get(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> idx, string key) =>
        idx.TryGetValue(key, out var i) && i < row.Count ? row[i] : string.Empty;

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        result.Add(current.ToString());
        return result;
    }
}
