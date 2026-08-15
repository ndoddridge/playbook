namespace Playbook.Core.Draft;

/// <summary>Pure comparison so the UI/service can react to genuinely new picks rather than
/// re-processing the whole board on every poll.</summary>
public static class DraftPickDiff
{
    /// <summary>Picks present in <paramref name="current"/> whose pick number was not yet made in
    /// <paramref name="previous"/>. Order preserved (pick-number ascending).</summary>
    public static IReadOnlyList<DraftPickRecord> DetectNewPicks(
        IReadOnlyList<DraftPickRecord> previous,
        IReadOnlyList<DraftPickRecord> current)
    {
        var previousMade = previous.Where(p => p.IsMade).Select(p => p.PickNumber).ToHashSet();
        return current
            .Where(p => p.IsMade && !previousMade.Contains(p.PickNumber))
            .OrderBy(p => p.PickNumber)
            .ToList();
    }
}
