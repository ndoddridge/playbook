using System.Globalization;
using Playbook.Core.Predictions.Models;

namespace Playbook.Tests;

/// <summary>
/// Real quarterback passing lines (2024 and 2025 regular seasons, 1,259 games), committed so the
/// QB-quality feature can be backtested deterministically without a network call.
///
/// Extracted from the nflverse weekly player-stats release. Attempts and passing EPA only — no
/// sportsbook column exists in the source for these rows, and none is carried here.
/// </summary>
internal static class RealQuarterbackFixture
{
    private const string FileName = "real_qb_lines_2024_2025.csv";

    internal static IReadOnlyList<QuarterbackGameLine> Load(int season) =>
        LoadAll().Where(l => l.Season == season).ToList();

    internal static IReadOnlyList<QuarterbackGameLine> LoadAll()
    {
        var path = Resolve();
        if (path is null)
        {
            return [];
        }

        var lines = new List<QuarterbackGameLine>();

        foreach (var row in File.ReadLines(path).Skip(1))
        {
            var parts = row.Split(',');
            if (parts.Length < 6)
            {
                continue;
            }

            lines.Add(new QuarterbackGameLine
            {
                Season = int.Parse(parts[0], CultureInfo.InvariantCulture),
                Week = int.Parse(parts[1], CultureInfo.InvariantCulture),
                Team = parts[2],
                PlayerId = parts[3],
                Attempts = int.Parse(parts[4], CultureInfo.InvariantCulture),
                PassingEpa = decimal.Parse(parts[5], CultureInfo.InvariantCulture)
            });
        }

        return lines;
    }

    private static string? Resolve()
    {
        var dir = AppContext.BaseDirectory;

        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "TestData", FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }
}
