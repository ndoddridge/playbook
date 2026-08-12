namespace Playbook.Core.Leagues;

public sealed class League
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required LeaguePlatform Platform { get; init; }

    public required LeagueType LeagueType { get; init; }

    public required ScoringType ScoringType { get; init; }

    public required int NumberOfTeams { get; init; }

    public required int CurrentWeek { get; init; }

    public required int Season { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>Platform league id (e.g. Sleeper numeric string). Null for pure mock leagues.</summary>
    public string? ExternalId { get; init; }

    public LeagueDataSource DataSource { get; init; } = LeagueDataSource.Mock;

    /// <summary>Reception points from platform scoring settings when known (0 / 0.5 / 1.0).</summary>
    public decimal? ReceptionPoints { get; init; }

    /// <summary>
    /// Sleeper/platform roster id for the user's own team in this league.
    /// Null means setup is incomplete for live leagues.
    /// </summary>
    public int? SelectedRosterId { get; init; }

    /// <summary>
    /// Platform-configured roster slots (e.g. QB,RB,RB,WR,WR,TE,FLEX,K,DEF,BN×6,IR×2).
    /// Empty for mock leagues (no platform settings exist). Taxi-squad slots are tracked
    /// separately by the platform and are intentionally NOT part of this list.
    /// </summary>
    public IReadOnlyList<string> RosterPositions { get; init; } = [];

    /// <summary>
    /// The league's configured roster size (count of <see cref="RosterPositions"/>), i.e. the
    /// number of NON-taxi roster spots a team may hold. Null when unknown (mock leagues, or a
    /// live league whose settings did not report roster positions).
    /// </summary>
    public int? RosterLimit => RosterPositions.Count > 0 ? RosterPositions.Count : null;

    public bool HasSelectedTeam => SelectedRosterId is int;

    public bool IsSetupComplete =>
        DataSource == LeagueDataSource.Mock || HasSelectedTeam;
}
