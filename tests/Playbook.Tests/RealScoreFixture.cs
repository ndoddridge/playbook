using Playbook.Core.Predictions.Models;

namespace Playbook.Tests;

/// <summary>
/// Real completed NFL final scores (2024 and 2025 regular seasons, 544 games), committed so the
/// model can be backtested deterministically without a network call.
///
/// Extracted from the nflverse schedules release. Scores only — the source file's spread_line,
/// total_line, moneyline and odds columns were deliberately not carried across, so no sportsbook
/// number can enter a test fixture and from there a model check.
/// </summary>
internal static class RealScoreFixture
{
    private const string FileName = "real_scores_2024_2025.csv";

    internal static IReadOnlyList<HistoricalGameScore> Load2025() => Load(2025);

    internal static IReadOnlyList<HistoricalGameScore> Load(int season) =>
        LoadAll().Where(g => g.Season == season).ToList();

    internal static IReadOnlyList<HistoricalGameScore> LoadAll()
    {
        var path = Resolve();
        if (path is null)
        {
            return [];
        }

        var games = new List<HistoricalGameScore>();

        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length < 7)
            {
                continue;
            }

            games.Add(new HistoricalGameScore
            {
                Season = int.Parse(parts[0]),
                Week = int.Parse(parts[1]),
                GameDate = DateOnly.Parse(parts[2]),
                AwayTeam = parts[3],
                AwayScore = int.Parse(parts[4]),
                HomeTeam = parts[5],
                HomeScore = int.Parse(parts[6]),
                GameId = parts.Length > 7 ? parts[7] : null
            });
        }

        return games;
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
