namespace Playbook.Application.Predictions;

public interface IQuickPicksSyncStatus
{
    string PropProvider { get; }

    string ConfiguredProvider { get; }

    /// <summary>Live | Mock | Fallback | Error | —</summary>
    string ProviderStatus { get; }

    bool UsedFallback { get; }

    int GamesLoaded { get; }

    int MarketsLoaded { get; }

    int PropsLoaded { get; }

    int PredictionsGenerated { get; }

    DateTimeOffset? LastPropSync { get; }

    DateTimeOffset? LastPredictionRun { get; }

    double AveragePredictionConfidence { get; }

    /// <summary>Last provider error message, if any.</summary>
    string? LastError { get; }

    /// <summary>Alias of <see cref="LastError"/> for existing monitor bindings.</summary>
    string? ProviderErrors { get; }

    TimeSpan? ProviderResponseTime { get; }
}

public sealed class QuickPicksSyncStatus : IQuickPicksSyncStatus
{
    private readonly object _gate = new();

    public string PropProvider { get; private set; } = "—";

    public string ConfiguredProvider { get; private set; } = "—";

    public string ProviderStatus { get; private set; } = "—";

    public bool UsedFallback { get; private set; }

    public int GamesLoaded { get; private set; }

    public int MarketsLoaded { get; private set; }

    public int PropsLoaded { get; private set; }

    public int PredictionsGenerated { get; private set; }

    public DateTimeOffset? LastPropSync { get; private set; }

    public DateTimeOffset? LastPredictionRun { get; private set; }

    public double AveragePredictionConfidence { get; private set; }

    public string? LastError { get; private set; }

    public string? ProviderErrors => LastError;

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
            LastError = string.IsNullOrWhiteSpace(error) ? null : error;

            ProviderStatus = ResolveStatus(activeProvider, usedFallback, error);
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

    private static string ResolveStatus(string activeProvider, bool usedFallback, string? error)
    {
        if (usedFallback)
        {
            return "Fallback";
        }

        if (!string.IsNullOrWhiteSpace(error) &&
            activeProvider.Equals("Mock", StringComparison.OrdinalIgnoreCase) == false)
        {
            return "Error";
        }

        if (activeProvider.Equals("TheOddsAPI", StringComparison.OrdinalIgnoreCase) ||
            activeProvider.Equals("Live", StringComparison.OrdinalIgnoreCase))
        {
            return "Live";
        }

        if (activeProvider.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            return "Mock";
        }

        return string.IsNullOrWhiteSpace(activeProvider) ? "—" : activeProvider;
    }
}
