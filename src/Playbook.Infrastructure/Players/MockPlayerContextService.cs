using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Core.Leagues;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Players;

/// <summary>
/// League-aware player context. Fantasy values stay mock until Decision engines land;
/// scoring and roster ownership follow the selected league (including live Sleeper).
/// </summary>
public sealed class MockPlayerContextService : IPlayerContextService
{
    private readonly IPlayerService _playerService;
    private readonly ILeagueState _leagueState;

    public MockPlayerContextService(IPlayerService playerService, ILeagueState leagueState)
    {
        _playerService = playerService;
        _leagueState = leagueState;
    }

    public PlayerContext? GetContext(Guid playerId)
    {
        var profile = _playerService.GetPlayerProfile(playerId);
        if (profile is null)
        {
            return null;
        }

        var league = _leagueState.CurrentLeague;
        var scoring = league?.ScoringType ?? ScoringType.Ppr;
        var fantasyTeam = _leagueState.FindTeamForPlayer(playerId);
        var seed = HashCode.Combine(playerId, scoring, league?.Id);

        var weeklyBase = profile.SeasonStats?.FantasyPoints is decimal points && profile.SeasonStats.GamesPlayed > 0
            ? points / profile.SeasonStats.GamesPlayed
            : 10m;

        var scoringBoost = scoring switch
        {
            ScoringType.Ppr => 1.12m,
            ScoringType.HalfPpr => 1.06m,
            _ => 1.0m
        };

        var weekly = Math.Round(weeklyBase * scoringBoost + (Math.Abs(seed % 17) / 10m), 1);
        var rosRank = 1 + Math.Abs(seed % 80);
        var posRank = 1 + Math.Abs(seed % 24);
        var vorp = Math.Round((weekly - 8m) + (Math.Abs(seed % 30) / 10m), 1);
        var confidence = 55 + Math.Abs(seed % 40);

        var leagueLabel = league?.Name ?? "No League Selected";
        var sourceLabel = league?.DataSource == LeagueDataSource.Sleeper ? "live Sleeper" : "demo";
        var rosterLabel = fantasyTeam is null
            ? "not on a tracked roster"
            : $"on {fantasyTeam.TeamName ?? fantasyTeam.DisplayName}";
        var summary =
            $"{profile.Player.FullName} projects as a {PlayerPresentation.PositionLabel(profile.Player.Position)}{posRank} " +
            $"in {leagueLabel} ({FormatScoring(scoring)}, {sourceLabel}; {rosterLabel}).";

        return new PlayerContext
        {
            Player = profile.Player,
            Profile = profile,
            League = league,
            FantasyTeam = fantasyTeam,
            ScoringType = scoring,
            WeeklyProjection = weekly,
            RestOfSeasonRank = rosRank,
            PositionalRank = posRank,
            ValueOverReplacement = vorp,
            RecommendationSummary = summary,
            Confidence = confidence
        };
    }

    private static string FormatScoring(ScoringType scoring) => scoring switch
    {
        ScoringType.Ppr => "PPR",
        ScoringType.HalfPpr => "Half PPR",
        ScoringType.Standard => "Standard",
        _ => scoring.ToString()
    };
}
