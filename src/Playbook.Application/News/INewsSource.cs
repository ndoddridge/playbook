using Playbook.Core.News;

namespace Playbook.Application.News;

/// <summary>
/// Internal source adapter. Prefer injecting <see cref="INewsProvider"/> in UI code.
/// </summary>
public interface INewsSource
{
    NewsProviderKind Kind { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<NewsArticle>> FetchAsync(CancellationToken cancellationToken = default);
}
