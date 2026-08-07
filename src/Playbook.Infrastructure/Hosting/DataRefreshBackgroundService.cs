using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.News;
using Playbook.Application.Players;

namespace Playbook.Infrastructure.Hosting;

/// <summary>
/// Periodically refreshes player data, news, and intelligence. Each step is logged separately.
/// </summary>
public sealed class DataRefreshBackgroundService : BackgroundService
{
    private readonly IPlayerService _players;
    private readonly INewsProvider _news;
    private readonly IIntelligenceService _intelligence;
    private readonly BackgroundRefreshOptions _options;
    private readonly ILogger<DataRefreshBackgroundService> _logger;

    public DataRefreshBackgroundService(
        IPlayerService players,
        INewsProvider news,
        IIntelligenceService intelligence,
        IOptions<BackgroundRefreshOptions> options,
        ILogger<DataRefreshBackgroundService> logger)
    {
        _players = players;
        _news = news;
        _intelligence = intelligence;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Background refresh is disabled via configuration");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(_options.IntervalMinutes, 1, 180));
        _logger.LogInformation("Background refresh starting; interval {Interval}", interval);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            RefreshPlayers();
            await RefreshNewsAsync(stoppingToken).ConfigureAwait(false);
            RefreshIntelligence();

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void RefreshPlayers()
    {
        try
        {
            _players.Refresh();
            _logger.LogInformation("Background refresh: player data updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh: player data update failed");
        }
    }

    private async Task RefreshNewsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _news.RefreshAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Background refresh: news data updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh: news data update failed");
        }
    }

    private void RefreshIntelligence()
    {
        try
        {
            _intelligence.Refresh();
            _logger.LogInformation("Background refresh: intelligence analysis updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh: intelligence analysis failed");
        }
    }
}
