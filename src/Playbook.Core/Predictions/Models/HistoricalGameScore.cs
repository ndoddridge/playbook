namespace Playbook.Core.Predictions.Models;

/// <summary>
/// One completed NFL game's real final score.
///
/// DELIBERATELY NARROW. The nflverse games.csv this is parsed from also carries spread_line,
/// total_line, moneylines and odds columns. None of them appear on this type, so a sportsbook
/// number cannot reach the model through this path even by accident. TeamPointsModelTests pins
/// that this type exposes no line/odds surface.
/// </summary>
public sealed record HistoricalGameScore
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required DateOnly GameDate { get; init; }

    public required string HomeTeam { get; init; }

    public required string AwayTeam { get; init; }

    public required int HomeScore { get; init; }

    public required int AwayScore { get; init; }

    /// <summary>Stable nflverse identifier, e.g. "2025_01_BAL_KC".</summary>
    public string? GameId { get; init; }

    public int PointsFor(string team) =>
        string.Equals(team, HomeTeam, StringComparison.OrdinalIgnoreCase) ? HomeScore : AwayScore;

    public int PointsAgainst(string team) =>
        string.Equals(team, HomeTeam, StringComparison.OrdinalIgnoreCase) ? AwayScore : HomeScore;

    public bool Involves(string team) =>
        string.Equals(team, HomeTeam, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(team, AwayTeam, StringComparison.OrdinalIgnoreCase);
}
