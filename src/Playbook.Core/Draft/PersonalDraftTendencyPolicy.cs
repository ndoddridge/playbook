using Playbook.Core.Historical;

namespace Playbook.Core.Draft;

/// <summary>
/// Pure, session-scoped read of the user's own picks — position mix, roster-build shape, and
/// (via <paramref name="decisionHistory"/>) how those picks compared to what Playbook recommended
/// at the time. Takes no store dependency: everything it needs is either on the live
/// <see cref="DraftBoard"/> or already accumulated by the caller, so a single mock can never
/// permanently alter anything — there is nowhere for this to persist to.
/// </summary>
public static class PersonalDraftTendencyPolicy
{
    /// <summary>Same evidence-strength bucketing used for league-wide historical intelligence
    /// (see HistoricalLeagueIntelligenceService.Strength) — "one observation = weak evidence"
    /// applies just as much to a single mock as it does to league history.</summary>
    private static HistoricalEvidenceStrength Strength(int n) => n switch
    {
        <= 0 => HistoricalEvidenceStrength.Unavailable,
        1 or 2 => HistoricalEvidenceStrength.Insufficient,
        <= 5 => HistoricalEvidenceStrength.Limited,
        <= 11 => HistoricalEvidenceStrength.Moderate,
        _ => HistoricalEvidenceStrength.Strong
    };

    /// <summary>Null when the user has made no picks yet in this draft — there is nothing to
    /// observe, so nothing is reported rather than an empty-but-present tendencies object.</summary>
    public static PersonalDraftTendencies? Compute(
        DraftBoard board, int myRosterId, IReadOnlyList<PersonalDraftDecision> decisionHistory)
    {
        var myPicks = board.Picks
            .Where(p => p.RosterId == myRosterId && p.IsMade)
            .OrderBy(p => p.PickNumber)
            .ToList();

        if (myPicks.Count == 0)
        {
            return null;
        }

        var positionEmphasis = myPicks
            .Where(p => p.PositionLabel is not null)
            .GroupBy(p => p.PositionLabel!)
            .Select(g => new PersonalPositionEmphasis(g.Key, g.Count(), g.Average(p => p.Round)))
            .OrderByDescending(x => x.PickCount)
            .ThenBy(x => x.AverageRound)
            .ToList();

        var categoryCounts = decisionHistory
            .Where(d => d.MatchedCategory is not null)
            .GroupBy(d => d.MatchedCategory!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return new PersonalDraftTendencies(
            myPicks.Count,
            positionEmphasis,
            DescribeRosterBuildPattern(positionEmphasis, myPicks.Count),
            categoryCounts,
            decisionHistory,
            Strength(myPicks.Count));
    }

    private static string DescribeRosterBuildPattern(IReadOnlyList<PersonalPositionEmphasis> emphasis, int totalPicks)
    {
        if (emphasis.Count == 0)
        {
            return "No positional pattern yet.";
        }

        var top = emphasis[0];
        var share = (double)top.PickCount / totalPicks;

        if (share >= 0.5)
        {
            return $"{top.Position}-heavy so far ({top.PickCount} of {totalPicks} picks)";
        }

        if (top.AverageRound <= 2 && top.Position is "RB" or "WR")
        {
            return $"Early {top.Position} priority";
        }

        return "Balanced positional mix so far";
    }
}
