using Microsoft.Extensions.Logging;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions.Models;
using Playbook.Infrastructure.Replay.Nflverse;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Real NFL final scores from the nflverse schedules release (games.csv) — the same file the
/// historical replay layer already downloads, so this adds no new external dependency and no API
/// key. Public dataset, stable schema, coverage from 1999.
///
/// COLUMN DISCIPLINE — games.csv also contains spread_line, total_line, away_moneyline,
/// home_moneyline, over_odds and under_odds. This parser reads NONE of them. Only season, week,
/// gameday, teams and final scores are extracted, so no sportsbook number can reach the model
/// through this path.
/// </summary>
public sealed class NflverseGameScoreProvider : IHistoricalGameScoreProvider
{
    private readonly NflverseCsvCache _cache;
    private readonly ILogger<NflverseGameScoreProvider> _logger;
    private readonly object _gate = new();

    private IReadOnlyList<HistoricalGameScore> _games = [];

    public NflverseGameScoreProvider(NflverseCsvCache cache, ILogger<NflverseGameScoreProvider> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public bool IsLoaded { get; private set; }

    public IReadOnlyList<HistoricalGameScore> GetCompletedGames(int season)
    {
        lock (_gate)
        {
            // Current season plus the previous one, which the model uses as a carry-over prior.
            return _games
                .Where(g => g.Season == season || g.Season == season - 1)
                .ToList();
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
            var parsed = Parse(lines);

            lock (_gate)
            {
                _games = parsed;
                IsLoaded = parsed.Count > 0;
            }

            _logger.LogInformation(
                "Historical scores loaded: {Games} completed regular-season games, seasons {First}-{Last}",
                parsed.Count,
                parsed.Count == 0 ? 0 : parsed.Min(g => g.Season),
                parsed.Count == 0 ? 0 : parsed.Max(g => g.Season));
        }
        catch (Exception ex)
        {
            // Score data is required for game markets. Failing to load means NO PLAY, never a
            // fabricated projection.
            _logger.LogWarning(ex, "Historical score load failed; game markets will remain unavailable");
        }
    }

    internal static IReadOnlyList<HistoricalGameScore> Parse(IReadOnlyList<string> lines)
    {
        if (lines.Count < 2)
        {
            return [];
        }

        var header = SplitCsv(lines[0]);
        int Idx(string name) => Array.FindIndex(header, h =>
            string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));

        var iSeason = Idx("season");
        var iWeek = Idx("week");
        var iType = Idx("game_type");
        var iDate = Idx("gameday");
        var iHome = Idx("home_team");
        var iAway = Idx("away_team");
        var iHomeScore = Idx("home_score");
        var iAwayScore = Idx("away_score");
        var iGameId = Idx("game_id");

        if (iSeason < 0 || iWeek < 0 || iHome < 0 || iAway < 0 || iHomeScore < 0 || iAwayScore < 0)
        {
            return [];
        }

        var results = new List<HistoricalGameScore>();

        for (var i = 1; i < lines.Count; i++)
        {
            var row = SplitCsv(lines[i]);
            if (row.Length <= iAwayScore)
            {
                continue;
            }

            // Regular season only — the model was fitted on REG games.
            if (iType >= 0 && !string.Equals(row[iType], "REG", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A blank score means the game has not been played. Skip; never impute.
            if (!int.TryParse(row[iHomeScore], out var homeScore) ||
                !int.TryParse(row[iAwayScore], out var awayScore) ||
                !int.TryParse(row[iSeason], out var season) ||
                !int.TryParse(row[iWeek], out var week))
            {
                continue;
            }

            DateOnly.TryParse(iDate >= 0 && iDate < row.Length ? row[iDate] : null, out var date);

            results.Add(new HistoricalGameScore
            {
                Season = season,
                Week = week,
                GameDate = date,
                HomeTeam = row[iHome].Trim(),
                AwayTeam = row[iAway].Trim(),
                HomeScore = homeScore,
                AwayScore = awayScore,
                GameId = iGameId >= 0 && iGameId < row.Length ? row[iGameId].Trim() : null
            });
        }

        return results;
    }

    /// <summary>Minimal CSV split honouring quoted fields (stadium names contain commas).</summary>
    private static string[] SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
