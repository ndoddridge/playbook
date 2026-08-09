using Playbook.Core.Decisions;

namespace Playbook.Core.Knowledge;

/// <summary>
/// Enforces temporal provenance: knowledge may only expose information
/// known at or before the information cutoff.
/// </summary>
public static class KnowledgeTemporalGuard
{
    public static bool IsKnownAtCutoff(DateTimeOffset? observedAt, DateTimeOffset? cutoff)
    {
        if (cutoff is null)
        {
            return true;
        }

        if (observedAt is null)
        {
            // Untimed observations are allowed only when the source already
            // claims cutoff-safety (historical snapshot fields). Callers must
            // not attach future-dated timestamps as null.
            return true;
        }

        return observedAt.Value <= cutoff.Value;
    }

    public static IReadOnlyList<KnowledgeEvidence> FilterEvidence(
        IEnumerable<KnowledgeEvidence> evidence,
        DateTimeOffset? cutoff) =>
        evidence.Where(e => IsKnownAtCutoff(e.ObservedAt, cutoff ?? e.InformationCutoff)).ToList();

    public static IReadOnlyList<KnowledgeFact> FilterFacts(
        IEnumerable<KnowledgeFact> facts,
        DateTimeOffset? cutoff) =>
        facts.Where(f => IsKnownAtCutoff(f.ObservedAt, cutoff)).ToList();

    public static IReadOnlyList<KnowledgeSignal> FilterSignals(
        IEnumerable<KnowledgeSignal> signals,
        DateTimeOffset? cutoff) =>
        signals.Where(s => IsKnownAtCutoff(s.ObservedAt, cutoff)).ToList();

    public static void AssertNoFutureLeak(
        SharedKnowledgeBundle bundle,
        DateTimeOffset? cutoff = null)
    {
        var bound = cutoff ?? bundle.InformationCutoff;
        if (bound is null)
        {
            return;
        }

        foreach (var fact in bundle.Facts)
        {
            if (fact.ObservedAt is DateTimeOffset observed && observed > bound.Value)
            {
                throw new InvalidOperationException(
                    $"Future fact leaked into knowledge for {bundle.PlayerName}: {fact.Statement}");
            }
        }

        foreach (var evidence in bundle.Evidence)
        {
            if (evidence.ObservedAt is DateTimeOffset observed && observed > bound.Value)
            {
                throw new InvalidOperationException(
                    $"Future evidence leaked into knowledge for {bundle.PlayerName}: {evidence.Statement}");
            }
        }
    }
}
