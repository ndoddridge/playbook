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
}
