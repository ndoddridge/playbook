namespace Playbook.Application.Intelligence;

public interface IIntelligenceSyncStatus
{
    int ArticlesProcessed { get; }

    int FactsGenerated { get; }

    TimeSpan? AnalyzerRuntime { get; }

    DateTimeOffset? LastAnalysisTime { get; }

    string? LastError { get; }
}

public sealed class IntelligenceSyncStatus : IIntelligenceSyncStatus
{
    private readonly object _gate = new();

    public int ArticlesProcessed { get; private set; }

    public int FactsGenerated { get; private set; }

    public TimeSpan? AnalyzerRuntime { get; private set; }

    public DateTimeOffset? LastAnalysisTime { get; private set; }

    public string? LastError { get; private set; }

    public void RecordSuccess(int articlesProcessed, int factsGenerated, TimeSpan runtime)
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

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            LastError = error;
        }
    }
}
