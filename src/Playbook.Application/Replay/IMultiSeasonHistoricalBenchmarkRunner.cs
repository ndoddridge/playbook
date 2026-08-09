using Playbook.Core.Replay;

namespace Playbook.Application.Replay;

/// <summary>
/// Runs identical frozen-model evaluation across multiple seasons.
/// Does not alter projection, decision, or confidence formulas.
/// </summary>
public interface IMultiSeasonHistoricalBenchmarkRunner
{
    Task<MultiSeasonBenchmarkReport> RunAsync(
        MultiSeasonBenchmarkRequest request,
        CancellationToken cancellationToken = default);
}
