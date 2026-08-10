namespace Playbook.Application.Projections;

public interface IProjectionSyncStatus
{
    string ProjectionEngine { get; }

    string Version { get; }

    int PlayersProjected { get; }

    int UniqueProjectionValues { get; }

    double AverageProjection { get; }

    double AverageProjectionConfidence { get; }

    double AverageVolatility { get; }

    TimeSpan? ProjectionRuntime { get; }

    DateTimeOffset? LastProjectionTime { get; }

    DateTimeOffset? LastProjectionRun { get; }

    string? LastError { get; }

    string? ProjectionErrors { get; }
}

public sealed class ProjectionSyncStatus : IProjectionSyncStatus
{
    private readonly object _gate = new();

    public string ProjectionEngine { get; private set; } = "Projection Engine";

    public string Version { get; private set; } = "—";

    public int PlayersProjected { get; private set; }

    public int UniqueProjectionValues { get; private set; }

    public double AverageProjection { get; private set; }

    public double AverageProjectionConfidence { get; private set; }

    public double AverageVolatility { get; private set; }

    public TimeSpan? ProjectionRuntime { get; private set; }

    public DateTimeOffset? LastProjectionTime { get; private set; }

    public DateTimeOffset? LastProjectionRun { get; private set; }

    public string? LastError { get; private set; }

    public string? ProjectionErrors { get; private set; }

    public void RecordSuccess(
        string engineName,
        string version,
        int playersProjected,
        int uniqueProjectionValues,
        double averageProjection,
        double averageConfidence,
        double averageVolatility,
        TimeSpan runtime)
    {
        lock (_gate)
        {
            ProjectionEngine = engineName;
            Version = version;
            PlayersProjected = playersProjected;
            UniqueProjectionValues = uniqueProjectionValues;
            AverageProjection = Math.Round(averageProjection, 2);
            AverageProjectionConfidence = Math.Round(averageConfidence, 2);
            AverageVolatility = Math.Round(averageVolatility, 2);
            ProjectionRuntime = runtime;
            LastProjectionTime = DateTimeOffset.Now;
            LastProjectionRun = DateTimeOffset.Now;
            LastError = null;
            ProjectionErrors = null;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            LastError = error;
            ProjectionErrors = error;
            LastProjectionRun = DateTimeOffset.Now;
        }
    }
}
