using Microsoft.Extensions.Logging;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions.Models;
using Playbook.Infrastructure.Replay.Nflverse;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Quarterback passing lines from the nflverse weekly player-stats release.
///
/// Reuses NflverseCsvCache (which already handles the .csv.gz format), so this adds no new
/// external dependency and no API key — the same discipline as the score provider.
///
/// Regular season only, matching how the coefficient was fitted.
/// </summary>
public sealed class NflverseQuarterbackFormProvider : IQuarterbackFormProvider
{
    private readonly NflverseCsvCache _cache;
    private readonly ILogger<NflverseQuarterbackFormProvider> _logger;
    private readonly object _gate = new();

    private readonly Dictionary<int, IReadOnlyList<QuarterbackGameLine>> _bySeason = [];

    public NflverseQuarterbackFormProvider(
        NflverseCsvCache cache,
        ILogger<NflverseQuarterbackFormProvider> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public bool IsLoaded { get; private set; }

    public IReadOnlyList<QuarterbackGameLine> GetQuarterbackLines(int season)
    {
        lock (_gate)
        {
            return _bySeason.TryGetValue(season, out var lines) ? lines : [];
        }
    }

    public async Task RefreshAsync(int season, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = string.Format(NflverseReleaseCatalog.WeeklyPlayerStatsUrl, season);
            var path = await _cache
                .EnsureFileAsync(url, $"stats_player_week_{season}.csv.gz", cancellationToken)
                .ConfigureAwait(false);

            var lines = await _cache.GetLinesAsync(path, cancellationToken).ConfigureAwait(false);
            var parsed = Parse(lines, season);

            lock (_gate)
            {
                _bySeason[season] = parsed;
                IsLoaded = IsLoaded || parsed.Count > 0;
            }

            _logger.LogInformation(
                "Quarterback form loaded for {Season}: {Lines} passing game lines", season, parsed.Count);
        }
        catch (Exception ex)
        {
            // Absent QB data is not fatal — the team-points model falls back to its baseline
            // coefficients. It must never fall back to an invented quarterback.
            _logger.LogWarning(ex, "Quarterback form load failed for {Season}", season);
        }
    }

    internal static IReadOnlyList<QuarterbackGameLine> Parse(IReadOnlyList<string> lines, int season)
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
        var iType = Idx("season_type");
        var iTeam = Idx("team");
        var iPlayer = Idx("player_id");
        var iPosition = Idx("position");
        var iAttempts = Idx("attempts");
        var iEpa = Idx("passing_epa");

        if (iWeek < 0 || iTeam < 0 || iPlayer < 0 || iAttempts < 0 || iEpa < 0)
        {
            return [];
        }

        var results = new List<QuarterbackGameLine>();

        for (var i = 1; i < lines.Count; i++)
        {
            var row = SplitCsv(lines[i]);
            if (row.Length <= Math.Max(iAttempts, iEpa))
            {
                continue;
            }

            // Regular season only, matching the fit.
            if (iType >= 0 && !string.Equals(row[iType], "REG", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (iPosition >= 0 && !string.Equals(row[iPosition], "QB", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(row[iAttempts], out var attempts) || attempts < 1)
            {
                continue;
            }

            if (!int.TryParse(row[iWeek], out var week))
            {
                continue;
            }

            // A missing EPA is treated as absent data, not as zero-value passing.
            if (!decimal.TryParse(
                    row[iEpa],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var epa))
            {
                continue;
            }

            var rowSeason = iSeason >= 0 && int.TryParse(row[iSeason], out var s) ? s : season;

            results.Add(new QuarterbackGameLine
            {
                Season = rowSeason,
                Week = week,
                Team = row[iTeam].Trim(),
                PlayerId = row[iPlayer].Trim(),
                Attempts = attempts,
                PassingEpa = epa
            });
        }

        return results;
    }

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
