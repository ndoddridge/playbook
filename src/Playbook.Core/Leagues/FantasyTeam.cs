namespace Playbook.Core.Leagues;

/// <summary>
/// One fantasy roster/team inside a league, with Playbook player ids on the roster.
/// </summary>
public sealed class FantasyTeam
{
    public required Guid LeagueId { get; init; }

    public required int RosterId { get; init; }

    public string? OwnerUserId { get; init; }

    public required string DisplayName { get; init; }

    public string? TeamName { get; init; }

    public IReadOnlyList<Guid> PlayerIds { get; init; } = [];

    public IReadOnlyList<Guid> StarterIds { get; init; } = [];

    public IReadOnlyList<string> ExternalPlayerIds { get; init; } = [];
}
