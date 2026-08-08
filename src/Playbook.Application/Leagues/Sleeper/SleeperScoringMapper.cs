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
