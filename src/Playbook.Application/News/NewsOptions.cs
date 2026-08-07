namespace Playbook.Application.News;

public enum NewsProviderKind
{
    Mock = 0,
    Live = 1
}

public sealed class NewsOptions
{
    public const string SectionName = "News";

    public NewsProviderKind Provider { get; set; } = NewsProviderKind.Mock;

    public EspnNewsOptions Espn { get; set; } = new();
}

public sealed class EspnNewsOptions
{
    public string BaseUrl { get; set; } = "https://site.api.espn.com/apis/site/v2/";

    /// <summary>Reserved for future authenticated news APIs.</summary>
    public string? ApiKey { get; set; }

    public int Limit { get; set; } = 40;

    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class BackgroundRefreshOptions
{
    public const string SectionName = "BackgroundRefresh";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 15;
}
