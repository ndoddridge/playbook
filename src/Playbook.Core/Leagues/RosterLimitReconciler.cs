namespace Playbook.Core.Leagues;

/// <summary>
/// Checks a roster against the league's configured limit. Never removes players — Playbook does
/// not own roster mutations against the platform (Sleeper), so "reconciliation" here means
/// correctly attributing which spots count against the limit (taxi squad is excluded — see
/// <see cref="FantasyTeam.CountedPlayerIds"/>) and surfacing a clear, deterministic warning when a
/// roster is still over limit after that attribution, so the user can resolve it on the platform.
/// </summary>
public static class RosterLimitReconciler
{
    public static RosterLimitStatus Check(FantasyTeam team, League league)
    {
        var limit = league.RosterLimit;
        var countedCount = team.CountedPlayerIds.Count;

        if (limit is null)
        {
            return new RosterLimitStatus
            {
                Limit = null,
                CountedCount = countedCount,
                TaxiExcludedCount = team.TaxiPlayerIds.Count,
                IsOverLimit = false,
                IsKnown = false,
                Message = "Roster limit unknown for this league (no platform roster settings available)."
            };
        }

        var overBy = countedCount - limit.Value;
        if (overBy <= 0)
        {
            return new RosterLimitStatus
            {
                Limit = limit,
                CountedCount = countedCount,
                TaxiExcludedCount = team.TaxiPlayerIds.Count,
                IsOverLimit = false,
                IsKnown = true,
                Message = $"Roster is within the configured limit ({countedCount}/{limit})."
            };
        }

        return new RosterLimitStatus
        {
            Limit = limit,
            CountedCount = countedCount,
            TaxiExcludedCount = team.TaxiPlayerIds.Count,
            IsOverLimit = true,
            OverBy = overBy,
            IsKnown = true,
            Message =
                $"Roster is {overBy} over the configured limit of {limit} ({countedCount} counted" +
                (team.TaxiPlayerIds.Count > 0 ? $", {team.TaxiPlayerIds.Count} taxi-squad player(s) already excluded" : string.Empty) +
                "). Playbook does not remove players automatically — resolve this on the platform (drop or move to taxi/IR), " +
                "then reconnect."
        };
    }
}

/// <summary>Result of checking one roster against its league's configured limit.</summary>
public sealed class RosterLimitStatus
{
    public required int? Limit { get; init; }

    /// <summary>Player count after excluding taxi squad — what actually counts against the limit.</summary>
    public required int CountedCount { get; init; }

    public required int TaxiExcludedCount { get; init; }

    public required bool IsOverLimit { get; init; }

    public int OverBy { get; init; }

    /// <summary>False when the league has no known roster limit (e.g. mock leagues).</summary>
    public required bool IsKnown { get; init; }

    public required string Message { get; init; }
}
