using Playbook.Core.News;

namespace Playbook.Application.News;

/// <summary>
/// Application-facing news catalog. UI consumes this only — never Mock/Live concretions.
/// </summary>
public interface INewsProvider
{
    string DisplayName { get; }

    IReadOnlyList<NewsArticle> GetLatest(int count = 12);

    IReadOnlyList<NewsArticle> GetForPlayer(Guid playerId, int count = 8);

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
