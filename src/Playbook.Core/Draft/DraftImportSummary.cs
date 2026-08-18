using Playbook.Core.Historical;

namespace Playbook.Core.Draft;

/// <summary>
/// Outcome of importing picks resolved from a screenshot. <see cref="SavedDraft"/> is set only
/// when the draft was marked complete and persisted to historical storage. <see cref="UnsavedMockDraft"/>
/// carries the same resolved-pick shape for an in-progress mock, purely in memory, for the
/// personal-tendencies computation — it never touches persistent storage.
/// </summary>
public sealed record DraftImportSummary(
    int SavedCount,
    int FlaggedCount,
    IReadOnlyList<string> FlaggedDetails,
    HistoricalLeagueDraft? SavedDraft,
    HistoricalLeagueDraft? UnsavedMockDraft);
