namespace Playbook.Application.Predictions;

public interface IQuickPicksSyncStatus
{
    string PropProvider { get; }

    string ConfiguredProvider { get; }

    bool UsedFallback { get; }

    int GamesLoaded { get; }

    int MarketsLoaded { get; }

    int PropsLoaded { get; }

    int PredictionsGenerated { get; }

    DateTimeOffset? LastPropSync { get; }

    DateTimeOffset? LastPredictionRun { get; }

    double AveragePredictionConfidence { get; }

    string? ProviderErrors { get; }

    TimeSpan? ProviderResponseTime { get; }
}

public sealed class QuickPicksSyncStatus : IQuickPicksSyncStatus
{
    private readonly object _gate = new();

    public string PropProvider { get; private set; } = "—";

    public string ConfiguredProvider { get; private set; } = "—";

    public bool UsedFallback { get; private set; }

    public int GamesLoaded { get; private set; }

    public int MarketsLoaded { get; private set; }

    public int PropsLoaded { get; private set; }

    public int PredictionsGenerated { get; private set; }

    public DateTimeOffset? LastPropSync { get; private set; }

    public DateTimeOffset? LastPredictionRun { get; private set; }

    public double AveragePredictionConfidence { get; private set; }

    public string? ProviderErrors { get; private set; }

    public TimeSpan? ProviderResponseTime { get; private set; }

    public void SetConfigured(string configured) => ConfiguredProvider = configured;

    public void RecordPropSync(
        string activeProvider,
        bool usedFallback,
        int games,
        int markets,
        int props,
        TimeSpan elapsed,
        string? error)
    {
        lock (_gate)
        {
            PropProvider = activeProvider;
            UsedFallback = usedFallback;
            GamesLoaded = games;
            MarketsLoaded = markets;
            PropsLoaded = props;
            LastPropSync = DateTimeOffset.Now;
            ProviderResponseTime = elapsed;
            ProviderErrors = string.IsNullOrWhiteSpace(error) ? null : error;
        }
    }

    public void RecordPredictions(int count, double averageConfidence)
    {
        lock (_gate)
        {
            PredictionsGenerated = count;
            AveragePredictionConfidence = averageConfidence;
            LastPredictionRun = DateTimeOffset.Now;
        }
    }
}
