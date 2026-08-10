namespace Playbook.Application.News;

public interface INewsSyncStatus
{
    string ConfiguredProvider { get; }

    string ActiveProvider { get; }

    DateTimeOffset? LastSuccessfulSync { get; }

    int ArticlesLoaded { get; }

    TimeSpan? ProviderResponseTime { get; }

    string? LastError { get; }

    bool UsedFallback { get; }
}

public sealed class NewsSyncStatus : INewsSyncStatus
{
    private readonly object _gate = new();

    public string ConfiguredProvider { get; private set; } = NewsProviderKind.Mock.ToString();

    public string ActiveProvider { get; private set; } = NewsProviderKind.Mock.ToString();

    public DateTimeOffset? LastSuccessfulSync { get; private set; }

    public int ArticlesLoaded { get; private set; }

    public TimeSpan? ProviderResponseTime { get; private set; }

    public string? LastError { get; private set; }

    public bool UsedFallback { get; private set; }

    public void SetConfigured(NewsProviderKind kind)
    {
        lock (_gate)
        {
            ConfiguredProvider = kind.ToString();
        }
    }

    public void RecordSuccess(
        NewsProviderKind active,
        int articlesLoaded,
        TimeSpan responseTime,
        bool usedFallback,
        string? priorError)
    {
        lock (_gate)
        {
            ActiveProvider = active.ToString();
            ArticlesLoaded = articlesLoaded;
            ProviderResponseTime = responseTime;
            LastSuccessfulSync = DateTimeOffset.Now;
            UsedFallback = usedFallback;
            LastError = priorError;
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
