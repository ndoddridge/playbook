using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.News;
using Playbook.Core.News;

namespace Playbook.Infrastructure.News;

/// <summary>
/// Live NFL news via ESPN's public site API. Auth slot reserved in <see cref="EspnNewsOptions.ApiKey"/>.
/// </summary>
public sealed class LiveNewsProvider : INewsSource
{
    public const string HttpClientName = "EspnNews";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NewsOptions _options;
    private readonly ILogger<LiveNewsProvider> _logger;

    public LiveNewsProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<NewsOptions> options,
        ILogger<LiveNewsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public NewsProviderKind Kind => NewsProviderKind.Live;

    public string DisplayName => "Live (ESPN)";

    public async Task<IReadOnlyList<NewsArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var limit = Math.Clamp(_options.Espn.Limit, 5, 50);
        var path = $"sports/football/nfl/news?limit={limit}";

        using var response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<EspnNewsResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var articles = (payload?.Articles ?? [])
            .Select(MapArticle)
            .Where(a => a is not null)
            .Cast<NewsArticle>()
            .OrderByDescending(a => a.Published)
            .ToList();

        stopwatch.Stop();
        _logger.LogInformation(
            "ESPN live news loaded {Count} articles in {ElapsedMs} ms",
            articles.Count,
            stopwatch.ElapsedMilliseconds);

        if (articles.Count == 0)
        {
            throw new InvalidOperationException("ESPN returned no news articles.");
        }

        return articles;
    }

    private static NewsArticle? MapArticle(EspnArticleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Headline))
        {
            return null;
        }

        var published = DateTimeOffset.TryParse(dto.Published, out var ts)
            ? ts
            : DateTimeOffset.UtcNow;

        var athleteNames = dto.Categories?
            .Where(c => string.Equals(c.Type, "athlete", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Description)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        var teamIds = dto.Categories?
            .Where(c => string.Equals(c.Type, "team", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Team?.Abbreviation ?? c.Description)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        var url = dto.Links?.Web?.Href ?? dto.Links?.Mobile?.Href;
        var summary = string.IsNullOrWhiteSpace(dto.Description)
            ? dto.Headline
            : dto.Description.Trim();

        return new NewsArticle
        {
            Id = ToDeterministicGuid(dto.Id?.ToString() ?? dto.Headline),
            Title = dto.Headline.Trim(),
            Summary = summary,
            Published = published,
            Source = "ESPN",
            Url = url,
            RelatedPlayerNames = athleteNames,
            RelatedTeamIds = teamIds,
            RelatedPlayerIds = [],
            Category = InferCategory(dto.Headline, summary),
            Priority = InferPriority(dto.Headline, summary, athleteNames.Count)
        };
    }

    private static NewsCategory InferCategory(string title, string summary)
    {
        var text = $"{title} {summary}";
        if (ContainsAny(text, "injur", "questionable", "doubtful", "IR ", "limited"))
        {
            return NewsCategory.Injury;
        }

        if (ContainsAny(text, "trade", "sign", "release", "cut", "acquire", "suspend"))
        {
            return NewsCategory.Transaction;
        }

        if (ContainsAny(text, "training camp", "camp:"))
        {
            return NewsCategory.TrainingCamp;
        }

        if (ContainsAny(text, "break", "sources:"))
        {
            return NewsCategory.Breaking;
        }

        if (ContainsAny(text, "analysis", "intel", "preview"))
        {
            return NewsCategory.Analysis;
        }

        return NewsCategory.General;
    }

    private static NewsPriority InferPriority(string title, string summary, int athleteCount)
    {
        var text = $"{title} {summary}";
        if (ContainsAny(text, "breaking", "suspend", "ruled out", "season-ending"))
        {
            return NewsPriority.Critical;
        }

        if (ContainsAny(text, "injur", "questionable", "trade") || athleteCount >= 3)
        {
            return NewsPriority.High;
        }

        if (ContainsAny(text, "training camp", "practice"))
        {
            return NewsPriority.Normal;
        }

        return NewsPriority.Normal;
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static Guid ToDeterministicGuid(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"playbook:espn:news:{value}"));
        return new Guid(bytes);
    }

    private sealed class EspnNewsResponse
    {
        [JsonPropertyName("articles")]
        public List<EspnArticleDto>? Articles { get; set; }
    }

    private sealed class EspnArticleDto
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("headline")]
        public string? Headline { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("published")]
        public string? Published { get; set; }

        [JsonPropertyName("links")]
        public EspnLinksDto? Links { get; set; }

        [JsonPropertyName("categories")]
        public List<EspnCategoryDto>? Categories { get; set; }
    }

    private sealed class EspnLinksDto
    {
        [JsonPropertyName("web")]
        public EspnHrefDto? Web { get; set; }

        [JsonPropertyName("mobile")]
        public EspnHrefDto? Mobile { get; set; }
    }

    private sealed class EspnHrefDto
    {
        [JsonPropertyName("href")]
        public string? Href { get; set; }
    }

    private sealed class EspnCategoryDto
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("team")]
        public EspnTeamDto? Team { get; set; }
    }

    private sealed class EspnTeamDto
    {
        [JsonPropertyName("abbreviation")]
        public string? Abbreviation { get; set; }
    }
}
