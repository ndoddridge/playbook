namespace Playbook.Core.Draft;

/// <summary>
/// Where one player sits within the natural value clusters of their position, among the
/// currently undrafted pool. Tiers are derived from real projection gaps in THIS draft's
/// remaining player pool — never assigned from rank alone (rank 12/13/14 can be one flat tier or
/// three separate ones depending on what the numbers actually look like).
/// </summary>
public sealed class PositionalTierInfo
{
    /// <summary>1 = the best remaining tier at this position.</summary>
    public required int TierRank { get; init; }

    /// <summary>How many undrafted players (including this one) share this tier.</summary>
    public required int PlayersInTier { get; init; }

    /// <summary>True when this is the last remaining player before a real value cliff.</summary>
    public required bool IsLastInTier { get; init; }
}

/// <summary>
/// Clusters a position's remaining, sorted-descending projections into tiers by detecting real
/// cliffs in the gaps between consecutive players — not by dividing rank into equal buckets.
///
/// METHOD: compute the gap between each consecutive pair of projections, then flag a cliff
/// wherever a gap is a clear outlier versus the position's own typical (median) gap. The median
/// is used rather than the mean because remaining-player pools are long-tailed — many
/// replacement-level players bunched a fraction of a point apart drag a mean down, which would
/// make ordinary noise look like a cliff. A gap must clear an absolute floor too, so a
/// dead-flat position (all projections equal) never manufactures tiers out of rounding noise.
/// </summary>
public static class PositionalTierPolicy
{
    /// <summary>Minimum points a gap must span before it can ever count as a cliff.</summary>
    public const decimal MinimumCliffPoints = 1.0m;

    /// <summary>A gap counts as a cliff once it exceeds this multiple of the position's median gap.</summary>
    public const decimal CliffMultiplier = 3.0m;

    /// <summary>
    /// Assigns a 1-based tier rank to each entry in <paramref name="sortedDescendingProjections"/>.
    /// The input MUST already be sorted highest-to-lowest; this does not re-sort, so callers keep
    /// control of tie-breaking.
    /// </summary>
    public static IReadOnlyList<int> AssignTiers(IReadOnlyList<decimal> sortedDescendingProjections)
    {
        var count = sortedDescendingProjections.Count;
        var tiers = new int[count];
        if (count == 0)
        {
            return tiers;
        }

        if (count == 1)
        {
            tiers[0] = 1;
            return tiers;
        }

        var gaps = new decimal[count - 1];
        for (var i = 0; i < count - 1; i++)
        {
            gaps[i] = Math.Max(0m, sortedDescendingProjections[i] - sortedDescendingProjections[i + 1]);
        }

        var medianGap = Median(gaps);
        var cliffThreshold = Math.Max(MinimumCliffPoints, medianGap * CliffMultiplier);

        var tier = 1;
        tiers[0] = tier;
        for (var i = 0; i < gaps.Length; i++)
        {
            if (gaps[i] > cliffThreshold)
            {
                tier++;
            }

            tiers[i + 1] = tier;
        }

        return tiers;
    }

    /// <summary>
    /// Builds per-player tier info for one position's remaining pool. Input order is preserved in
    /// the output; only the projection values need to already be sorted descending for tiering to
    /// be meaningful (callers should pass players already ranked by projection).
    /// </summary>
    public static IReadOnlyList<PositionalTierInfo> BuildTierInfo(IReadOnlyList<decimal> sortedDescendingProjections)
    {
        var tierRanks = AssignTiers(sortedDescendingProjections);
        var countByTier = tierRanks.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());

        var result = new PositionalTierInfo[tierRanks.Count];
        for (var i = 0; i < tierRanks.Count; i++)
        {
            var thisTier = tierRanks[i];
            var isLast = i == tierRanks.Count - 1 || tierRanks[i + 1] != thisTier;
            result[i] = new PositionalTierInfo
            {
                TierRank = thisTier,
                PlayersInTier = countByTier[thisTier],
                IsLastInTier = isLast
            };
        }

        return result;
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2m
            : sorted[mid];
    }
}
