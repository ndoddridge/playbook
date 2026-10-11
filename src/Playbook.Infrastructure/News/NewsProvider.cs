using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.News;
using Playbook.Application.Players;
using Playbook.Core.News;

namespace Playbook.Infrastructure.News;

/// <summary>
/// UI-facing news facade. Selects Mock/Live from configuration and falls back to mock on failure.
/// Maps related athlete names onto Playbook player ids when the source lacks them.
/// </summary>
public sealed class NewsProvider : INewsProvider
{
    private readonly INewsSource _primary;
    private readonly MockNewsProvider _fallback;
    private readonly NewsSyncStatus _status;
    private readonly IPlayerService _players;
    private readonly ILogger<NewsProvider> _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Task? _browserLoad;

    private IReadOnlyList<NewsArticle> _articles = [];
    private bool _loaded;

    public NewsProvider(
        IEnumerable<INewsSource> sources,
        MockNewsProvider fallback,
        NewsSyncStatus status,
        IOptions<NewsOptions> options,
        IPlayerService players,
        ILogger<NewsProvider> logger)
    {
        _fallback = fallback;
        _status = status;
        _players = players;
        _logger = logger;

        var configured = options.Value.Provider;
        _status.SetConfigured(configured);
        _primary = sources.FirstOrDefault(s => s.Kind == configured) ?? fallback;
        DisplayName = _primary.DisplayName;
    }

    public string DisplayName { get; private set; }

    public IReadOnlyList<NewsArticle> GetLatest(int count = 12)
    {
        EnsureLoaded();
        return _articles.Take(Math.Max(1, count)).ToList();
    }

    public IReadOnlyList<NewsArticle> GetForPlayer(Guid playerId, int count = 8)
    {
        EnsureLoaded();
        return _articles
            .Where(a => a.RelatedPlayerIds.Contains(playerId))
            .Take(Math.Max(1, count))
            .ToList();
    }

    public NewsArticle? GetById(Guid articleId)
    {
        EnsureLoaded();
        return _articles.FirstOrDefault(a => a.Id == articleId);
    }

    public IReadOnlyList<NewsArticle> GetByIds(IEnumerable<Guid> articleIds)
    {
        EnsureLoaded();
        var set = articleIds.ToHashSet();
        return _articles.Where(a => set.Contains(a.Id)).ToList();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadCatalogAsync().ConfigureAwait(false);
            _loaded = true;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        // The browser build cannot block on a network call; see PlayerService.EnsureLoaded.
        if (OperatingSystem.IsBrowser())
        {
            _browserLoad ??= RefreshAsync();
            return;
        }

        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            LoadCatalog();
            _loaded = true;
        }
    }

    private void LoadCatalog() => LoadCatalogAsync().GetAwaiter().GetResult();

    private async Task LoadCatalogAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        string? error = null;

        try
        {
            var articles = await _primary.FetchAsync().ConfigureAwait(false);
            stopwatch.Stop();
            if (articles.Count == 0)
            {
                throw new InvalidOperationException($"{_primary.DisplayName} returned no articles.");
            }

            _articles = EnrichWithPlayerIds(articles);
            DisplayName = _primary.DisplayName;
            _status.RecordSuccess(_primary.Kind, _articles.Count, stopwatch.Elapsed, false, null);
            _logger.LogInformation(
                "News catalog loaded from {Provider} ({Count} articles, {ElapsedMs} ms)",
                _primary.DisplayName,
                _articles.Count,
                stopwatch.ElapsedMilliseconds);
            return;
        }
        catch (Exception ex) when (_primary.Kind != NewsProviderKind.Mock)
        {
            stopwatch.Stop();
            error = $"{_primary.DisplayName} failed: {ex.Message}";
            _status.RecordFailure(error);
            _logger.LogWarning(ex, "Live news provider failed; falling back to mock news");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            error = $"{_primary.DisplayName} failed: {ex.Message}";
            _status.RecordFailure(error);
            _logger.LogError(ex, "Mock news provider failed unexpectedly");
            throw;
        }

        var fallbackWatch = Stopwatch.StartNew();
        var mock = await _fallback.FetchAsync().ConfigureAwait(false);
        fallbackWatch.Stop();
        _articles = EnrichWithPlayerIds(mock);
        DisplayName = $"{_fallback.DisplayName} (fallback)";
        _status.RecordSuccess(NewsProviderKind.Mock, _articles.Count, fallbackWatch.Elapsed, true, error);
        _logger.LogInformation(
            "News catalog served from mock fallback ({Count} articles)",
            _articles.Count);
    }

    private IReadOnlyList<NewsArticle> EnrichWithPlayerIds(IReadOnlyList<NewsArticle> articles)
    {
        var catalog = _players.GetAllPlayers();
        return articles.Select(article =>
        {
            if (article.RelatedPlayerIds.Count > 0 || article.RelatedPlayerNames.Count == 0)
            {
                return article;
            }

            var ids = new List<Guid>();
            foreach (var name in article.RelatedPlayerNames)
            {
                var match = catalog.FirstOrDefault(p =>
                    p.FullName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    $"{p.FirstName} {p.LastName}".Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    p.LastName.Equals(name, StringComparison.OrdinalIgnoreCase));

                // Prefer stronger matches for common last names via contains on full name.
                match ??= catalog.FirstOrDefault(p =>
                    name.Contains(p.LastName, StringComparison.OrdinalIgnoreCase) &&
                    name.Contains(p.FirstName, StringComparison.OrdinalIgnoreCase));

                if (match is not null && !ids.Contains(match.Id))
                {
                    ids.Add(match.Id);
                }
            }

            return new NewsArticle
            {
                Id = article.Id,
                Title = article.Title,
                Summary = article.Summary,
                Published = article.Published,
                Source = article.Source,
                Url = article.Url,
                RelatedPlayerIds = ids,
                RelatedTeamIds = article.RelatedTeamIds,
                RelatedPlayerNames = article.RelatedPlayerNames,
                Category = article.Category,
                Priority = article.Priority
            };
        }).ToList();
    }
}
