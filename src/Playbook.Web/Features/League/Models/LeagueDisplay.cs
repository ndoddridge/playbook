using Playbook.Core.Leagues;

namespace Playbook.Web.Features.League.Models;

public static class LeagueDisplay
{
    public static string Platform(LeaguePlatform platform) => platform switch
    {
        LeaguePlatform.Sleeper => "Sleeper",
        LeaguePlatform.ESPN => "ESPN",
        LeaguePlatform.Yahoo => "Yahoo",
        LeaguePlatform.NFL => "NFL.com",
        _ => platform.ToString()
    };

    public static string LeagueType(LeagueType leagueType) => leagueType switch
    {
        Core.Leagues.LeagueType.Redraft => "Redraft",
        Core.Leagues.LeagueType.Dynasty => "Dynasty",
        Core.Leagues.LeagueType.Keeper => "Keeper",
        _ => leagueType.ToString()
    };

    public static string Scoring(ScoringType scoringType) => scoringType switch
    {
        ScoringType.Standard => "Standard",
        ScoringType.HalfPpr => "Half PPR",
        ScoringType.Ppr => "PPR",
        _ => scoringType.ToString()
    };

    public static string ScoringDetailed(Core.Leagues.League league)
    {
        var baseLabel = Scoring(league.ScoringType);
        return league.ReceptionPoints is decimal rec
            ? $"{baseLabel} ({rec:0.##} rec)"
            : baseLabel;
    }

    public static string DataSource(LeagueDataSource source) => source switch
    {
        LeagueDataSource.Sleeper => "Live Sleeper",
        _ => "Mock demo"
    };

    public static bool IsLive(Core.Leagues.League league) => league.DataSource == LeagueDataSource.Sleeper;
}
