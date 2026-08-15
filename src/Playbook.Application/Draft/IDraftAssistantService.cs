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
}
