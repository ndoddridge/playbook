using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.News;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats.Interfaces;

namespace Playbook.Infrastructure.Hosting;

/// <summary>
/// Periodically refreshes players, stats, news, injuries, intelligence, and projections.
/// </summary>
public sealed class DataRefreshBackgroundService : BackgroundService
{
    private readonly IPlayerService _players;
    private readonly IPlayerStatsService _stats;
    private readonly INewsProvider _news;
    private readonly IPlayerInjuryService _injuries;
    private readonly IIntelligenceService _intelligence;
    private readonly IProjectionService _projections;
    private readonly BackgroundRefreshOptions _options;
    private readonly ILogger<DataRefreshBackgroundService> _logger;

    public DataRefreshBackgroundService(
        IPlayerService players,
        IPlayerStatsService stats,
        INewsProvider news,
        IPlayerInjuryService injuries,
        IIntelligenceService intelligence,
        IProjectionService projections,
        IOptions<BackgroundRefreshOptions> options,
        ILogger<DataRefreshBackgroundService> logger)
    {
        _players = players;
        _stats = stats;
        _news = news;
        _injuries = injuries;
        _intelligence = intelligence;
        _projections = projections;
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
            await RefreshStatsAsync(stoppingToken).ConfigureAwait(false);
            await RefreshNewsAsync(stoppingToken).ConfigureAwait(false);
            await RefreshInjuriesAsync(stoppingToken).ConfigureAwait(false);
            RefreshIntelligence();
            RefreshProjections();

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

    private async Task RefreshStatsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _stats.RefreshAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Background refresh: player stats updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh: player stats update failed");
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

    private async Task RefreshInjuriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _injuries.RefreshAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Background refresh: injury data updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh: injury data update failed");
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

    private void RefreshProjections()
    {
        try
        {
            _projections.Refresh();
            _logger.LogInformation("Background refresh: projections updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh: projection update failed");
        }
    }
}
