namespace Playbook.Application.Projections;

public interface IProjectionSyncStatus
{
    int PlayersProjected { get; }

    TimeSpan? ProjectionRuntime { get; }

    double AverageProjectionConfidence { get; }

    DateTimeOffset? LastProjectionTime { get; }

    string? LastError { get; }
}

public sealed class ProjectionSyncStatus : IProjectionSyncStatus
{
    private readonly object _gate = new();

    public int PlayersProjected { get; private set; }

    public TimeSpan? ProjectionRuntime { get; private set; }

    public double AverageProjectionConfidence { get; private set; }

    public DateTimeOffset? LastProjectionTime { get; private set; }

    public string? LastError { get; private set; }

    public void RecordSuccess(int playersProjected, double averageConfidence, TimeSpan runtime)
    {
        lock (_gate)
        {
            PlayersProjected = playersProjected;
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
