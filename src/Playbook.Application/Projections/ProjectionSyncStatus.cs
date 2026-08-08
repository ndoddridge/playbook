namespace Playbook.Application.Projections;

public interface IProjectionSyncStatus
{
    int PlayersProjected { get; }

    int UniqueProjectionValues { get; }

    double AverageProjection { get; }

    double AverageProjectionConfidence { get; }

    TimeSpan? ProjectionRuntime { get; }

    DateTimeOffset? LastProjectionTime { get; }

    string? LastError { get; }
}

public sealed class ProjectionSyncStatus : IProjectionSyncStatus
{
    private readonly object _gate = new();

    public int PlayersProjected { get; private set; }

    public int UniqueProjectionValues { get; private set; }

    public double AverageProjection { get; private set; }

    public double AverageProjectionConfidence { get; private set; }

    public TimeSpan? ProjectionRuntime { get; private set; }

    public DateTimeOffset? LastProjectionTime { get; private set; }

    public string? LastError { get; private set; }

    public void RecordSuccess(
        int playersProjected,
        int uniqueProjectionValues,
        double averageProjection,
        double averageConfidence,
        TimeSpan runtime)
    {
        lock (_gate)
        {
            PlayersProjected = playersProjected;
            UniqueProjectionValues = uniqueProjectionValues;
            AverageProjection = Math.Round(averageProjection, 2);
            AverageProjectionConfidence = Math.Round(averageConfidence, 2);
            ProjectionRuntime = runtime;
            LastProjectionTime = DateTimeOffset.Now;
            LastError = null;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            LastError = error;
        }
    }
}
