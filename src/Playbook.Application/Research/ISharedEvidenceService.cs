using Playbook.Core.Research;

namespace Playbook.Application.Research;

/// <summary>
/// Read-side transformation of the existing permanent research memory
/// (<see cref="IPredictionResearchStore"/>) into structured, attributable
/// <see cref="PlayerEvidenceItem"/>s. Computed on demand from the single existing store — no new
/// persistent storage, no per-feature evidence stores. Never mutates frozen model weights,
/// Projection V2, Confidence V2, or the Decision Policy; consumers decide what (if anything) to
/// do with the evidence they're given.
/// </summary>
public interface ISharedEvidenceService
{
    /// <summary>All graded evidence for one player, most-recent first. Empty when none exists yet.</summary>
    PlayerEvidenceSummary GetEvidenceForPlayer(Guid playerId);
}
