using Playbook.Core.Draft;

namespace Playbook.Application.Draft;

/// <summary>
/// Live draft board + pick recommendations for the currently connected Sleeper league. Read-only
/// — never writes anything back to Sleeper. Stateless per call; the caller (UI) controls poll
/// cadence.
/// </summary>
public interface IDraftAssistantService
{
    Task<DraftAssistantReport> GetReportAsync(CancellationToken cancellationToken = default);

    /// <summary>Current dynasty posture for a league. Ignored by redraft leagues.</summary>
    DynastyStrategy GetStrategy(Guid leagueId);

    /// <summary>Set the dynasty posture. Takes effect on the next report.</summary>
    void SetStrategy(Guid leagueId, DynastyStrategy strategy);

    /// <summary>
    /// Follow a specific Sleeper draft by its Draftboard URL or raw draft id, instead of
    /// resolving a draft from the connected league. This is what lets Playbook follow a mock
    /// draft or a draft in a league the user has not connected.
    /// Returns false when the input does not contain a usable Sleeper draft id.
    /// </summary>
    bool AttachDraft(string draftUrlOrId);

    /// <summary>Stop following a directly-attached draft and fall back to the connected league.</summary>
    void DetachDraft();

    /// <summary>Draft id currently being followed directly, or null when using the league's draft.</summary>
    string? AttachedDraftId { get; }
}
