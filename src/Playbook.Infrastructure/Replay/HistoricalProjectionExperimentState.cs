using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Per-DI-container switch for which projection feeds decisions.
/// Default remains Projection V1 so frozen benchmarks stay unchanged.
/// </summary>
public sealed class HistoricalProjectionExperimentState
{
    public HistoricalProjectionPrimaryMode PrimaryMode { get; set; } =
        HistoricalProjectionPrimaryMode.ProjectionV1;
}
