namespace Playbook.Application.Leagues.Sleeper;

public interface ISleeperLeagueClient
{
    Task<SleeperLeagueSnapshot?> GetLeagueSnapshotAsync(
        string leagueId,
        CancellationToken cancellationToken = default);
}

public sealed class SleeperLeagueSnapshot
{
    public required string ExternalLeagueId { get; init; }
    public required string Name { get; init; }
    public required string Season { get; init; }
    public required string Status { get; init; }
    public required int TeamCount { get; init; }
    public required int CurrentWeek { get; init; }
    public required int SleeperLeagueType { get; init; }
    public required IReadOnlyDictionary<string, double> ScoringSettings { get; init; }
    public required IReadOnlyList<string> RosterPositions { get; init; }
    public required IReadOnlyList<SleeperRosterSnapshot> Rosters { get; init; }
}

public sealed class SleeperRosterSnapshot
{
    public required int RosterId { get; init; }
    public required string? OwnerId { get; init; }
    public required string TeamName { get; init; }
    public required string OwnerName { get; init; }
    public required IReadOnlyList<string> SleeperPlayerIds { get; init; }
    public required IReadOnlyList<string> StarterSleeperPlayerIds { get; init; }
    public required IReadOnlyList<string> ReserveSleeperPlayerIds { get; init; }
    public required IReadOnlyList<string> TaxiSleeperPlayerIds { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Ties { get; init; }
    public double FantasyPoints { get; init; }
}
