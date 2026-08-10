using Playbook.Core.Replay;

namespace Playbook.Application.Replay;

/// <summary>
/// Developer-facing historical replay entry point.
/// Example: <c>await runner.RunAsync(new HistoricalReplayRequest { Season = 2018, Week = 7 });</c>
/// </summary>
public interface IHistoricalReplayRunner
{
    Task<HistoricalReplayReport> RunAsync(
        HistoricalReplayRequest request,
        CancellationToken cancellationToken = default);
}
