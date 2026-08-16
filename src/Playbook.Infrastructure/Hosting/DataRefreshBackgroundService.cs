using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.News;
using Playbook.Application.Players;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Research;
using Playbook.Application.Stats.Interfaces;

namespace Playbook.Infrastructure.Hosting;

/// <summary>
/// Periodically refreshes players, stats, news, injuries, intelligence, projections, Quick Picks,
/// and grades any Quick Pick snapshots whose game has concluded.
/// </summary>
public sealed class DataRefreshBackgroundService : BackgroundService
{
    private readonly IPlayerService _players;
    private readonly IPlayerStatsService _stats;
    private readonly INewsProvider _news;
    private readonly IPlayerInjuryService _injuries;
    private readonly IIntelligenceService _intelligence;
    private readonly IProjectionService _projections;
    private readonly IQuickPicksService _quickPicks;
    private readonly IHistoricalGameScoreProvider _gameScores;
    private readonly IQuarterbackFormProvider _quarterbackForm;
    private readonly IPostEventReconciliationService _reconciliation;
    private readonly BackgroundRefreshOptions _options;
    private readonly ILogger<DataRefreshBackgroundService> _logger;

    public DataRefreshBackgroundService(
        IPlayerService players,
        IPlayerStatsService stats,
        INewsProvider news,
        IPlayerInjuryService injuries,
        IIntelligenceService intelligence,
        IProjectionService projections,
        IQuickPicksService quickPicks,
        IHistoricalGameScoreProvider gameScores,
        IQuarterbackFormProvider quarterbackForm,
        IPostEventReconciliationService reconciliation,
        IOptions<BackgroundRefreshOptions> options,
        ILogger<DataRefreshBackgroundService> logger)
    {
        _players = players;
        _stats = stats;
        _news = news;
        _injuries = injuries;
        _intelligence = intelligence;
        _projections = projections;
        _quickPicks = quickPicks;
        _gameScores = gameScores;
        _quarterbackForm = quarterbackForm;
        _reconciliation = reconciliation;
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
            await RefreshGameScoresAsync(stoppingToken).ConfigureAwait(false);
            RefreshQuickPicks();
            RunPostEventReconciliation();

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

    /// <summary>
    /// Real NFL final scores feed the team-points model. Refreshed before Quick Picks so a
    /// newly completed week is reflected in the next projection. A failure here leaves the
    /// model without history, which surfaces as NO PLAY rather than a stale or invented number.
    /// </summary>
    private async Task RefreshGameScoresAsync(CancellationToken cancellationToken)
    {
        try
        {
            var season = DateTime.UtcNow.Year;
            await _gameScores.RefreshAsync(season, cancellationToken).ConfigureAwait(false);

            // Current season plus the prior one, matching the window the feature builder reads.
            await _quarterbackForm.RefreshAsync(season, cancellationToken).ConfigureAwait(false);
            await _quarterbackForm.RefreshAsync(season - 1, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Background refresh: historical NFL scores + quarterback form updated");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh: historical NFL score update failed");
        }
    }

    private void RefreshQuickPicks()
    {
        try
        {
            _quickPicks.Refresh();
            _logger.LogInformation("Background refresh: Quick Picks updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh: Quick Picks update failed");
        }
    }

    private void RunPostEventReconciliation()
    {
        try
        {
            var graded = _reconciliation.RunPendingReconciliation();
            if (graded > 0)
            {
                _logger.LogInformation(
                    "Background refresh: postgame reconciliation graded {Count} prediction snapshot(s)", graded);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh: postgame reconciliation failed");
        }
    }
}
