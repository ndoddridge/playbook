using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues.Sleeper;

/// <summary>
/// Maps Sleeper scoring_settings into Playbook league scoring without hard-coding one format.
/// </summary>
public static class SleeperScoringMapper
{
    public static (ScoringType Format, decimal ReceptionPoints) MapReceptionScoring(
        IReadOnlyDictionary<string, double>? scoringSettings)
    {
        var receptionPoints = (decimal)GetSetting(scoringSettings, "rec");
        var format = receptionPoints switch
        {
            >= 0.9m => ScoringType.Ppr,
            >= 0.4m => ScoringType.HalfPpr,
            _ => ScoringType.Standard
        };

        return (format, receptionPoints);
    }

    public static LeagueType MapLeagueType(int sleeperType) =>
        sleeperType switch
        {
            1 => LeagueType.Keeper,
            2 => LeagueType.Dynasty,
            _ => LeagueType.Redraft
        };

    /// <summary>
    /// Map a Sleeper draft's metadata.scoring_type name (e.g. "ppr", "half_ppr", "2qb",
    /// "dynasty_2qb", "std") to a Playbook scoring format. Used when following a draft directly,
    /// where the detailed scoring_settings dictionary a league exposes is not available.
    /// Unrecognised names fall back to PPR, which is Sleeper's own default.
    /// </summary>
    public static ScoringType MapScoringTypeFromName(string? scoringTypeName)
    {
        if (string.IsNullOrWhiteSpace(scoringTypeName))
        {
            return ScoringType.Ppr;
        }

        var name = scoringTypeName.Trim().ToLowerInvariant();

        if (name.Contains("half", StringComparison.Ordinal))
        {
            return ScoringType.HalfPpr;
        }

        // "std"/"standard" are the only non-reception formats Sleeper names directly.
        if (name.Contains("std", StringComparison.Ordinal) ||
            name.Contains("standard", StringComparison.Ordinal))
        {
            return ScoringType.Standard;
        }

        return ScoringType.Ppr;
    }

    public static string FormatLabel(ScoringType format, decimal receptionPoints) =>
        format switch
        {
            ScoringType.Ppr => $"PPR ({receptionPoints:0.##} rec)",
            ScoringType.HalfPpr => $"Half PPR ({receptionPoints:0.##} rec)",
            _ => receptionPoints <= 0
                ? "Standard (0 rec)"
                : $"Custom ({receptionPoints:0.##} rec)"
        };

    private static double GetSetting(IReadOnlyDictionary<string, double>? settings, string key) =>
        settings is not null && settings.TryGetValue(key, out var value)
            ? value
            : 0d;
}
