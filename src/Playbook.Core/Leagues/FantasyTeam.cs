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

    /// <summary>Every player on the roster, including taxi squad and IR/reserve.</summary>
    public IReadOnlyList<Guid> PlayerIds { get; init; } = [];

    public IReadOnlyList<Guid> StarterIds { get; init; } = [];

    /// <summary>
    /// Taxi-squad player ids (dynasty leagues). Governed by a separate platform slot count,
    /// not part of <see cref="League.RosterPositions"/> — excluded when checking the roster
    /// limit. Subset of <see cref="PlayerIds"/>.
    /// </summary>
    public IReadOnlyList<Guid> TaxiPlayerIds { get; init; } = [];

    /// <summary>IR/reserve player ids. Subset of <see cref="PlayerIds"/>.</summary>
    public IReadOnlyList<Guid> ReservePlayerIds { get; init; } = [];

    public IReadOnlyList<string> ExternalPlayerIds { get; init; } = [];

    /// <summary>
    /// Roster spots that count against <see cref="League.RosterLimit"/>. <see cref="League.RosterLimit"/>
    /// is derived from Sleeper's <c>roster_positions</c> array, which does not include IR/reserve or
    /// taxi squad — those are separate platform slot counts (<c>reserve_slots</c> / <c>taxi_slots</c>)
    /// — so both are excluded here too, or the over-limit check would flag a roster that the
    /// platform itself considers legal.
    /// </summary>
    public IReadOnlyList<Guid> CountedPlayerIds =>
        TaxiPlayerIds.Count == 0 && ReservePlayerIds.Count == 0
            ? PlayerIds
            : PlayerIds.Where(id => !TaxiPlayerIds.Contains(id) && !ReservePlayerIds.Contains(id)).ToList();
}
