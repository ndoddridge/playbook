namespace Playbook.Application.Intelligence;

public interface IIntelligenceSyncStatus
{
    int ArticlesProcessed { get; }

    int FactsGenerated { get; }

    TimeSpan? AnalyzerRuntime { get; }

    DateTimeOffset? LastAnalysisTime { get; }

    int ProfilesGenerated { get; }

    int FactsAggregated { get; }

    double AverageFactsPerPlayer { get; }

    TimeSpan? AggregationRuntime { get; }

    string? LastError { get; }
}

public sealed class IntelligenceSyncStatus : IIntelligenceSyncStatus
{
    private readonly object _gate = new();

    public int ArticlesProcessed { get; private set; }

    public int FactsGenerated { get; private set; }

    public TimeSpan? AnalyzerRuntime { get; private set; }

    public DateTimeOffset? LastAnalysisTime { get; private set; }

    public int ProfilesGenerated { get; private set; }

    public int FactsAggregated { get; private set; }

    public double AverageFactsPerPlayer { get; private set; }

    public TimeSpan? AggregationRuntime { get; private set; }

    public string? LastError { get; private set; }

    public void RecordAnalysisSuccess(int articlesProcessed, int factsGenerated, TimeSpan runtime)
    {
        lock (_gate)
        {
            ArticlesProcessed = articlesProcessed;
            FactsGenerated = factsGenerated;
            AnalyzerRuntime = runtime;
            LastAnalysisTime = DateTimeOffset.Now;
            LastError = null;
        }
    }

    public void RecordAggregationSuccess(int profilesGenerated, int factsAggregated, TimeSpan runtime)
    {
        lock (_gate)
        {
            ProfilesGenerated = profilesGenerated;
            FactsAggregated = factsAggregated;
            AverageFactsPerPlayer = profilesGenerated <= 0
                ? 0
                : Math.Round((double)factsAggregated / profilesGenerated, 2);
            AggregationRuntime = runtime;
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
