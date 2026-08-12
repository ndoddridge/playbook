using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Playbook.Application.Leagues;

namespace Playbook.Infrastructure.Hosting;

/// <summary>
/// Reconnects previously-connected Sleeper leagues on process start so the uploaded team is
/// still there after a restart/redeploy without the user re-entering the league id. Runs once,
/// immediately (no delay) — unlike <see cref="DataRefreshBackgroundService"/>'s periodic data
/// refresh, the user is waiting for their team to appear. Never creates or falls back to a mock
/// team: if reconnect fails, the league/team simply stays unselected until the next successful
/// connect (manual or on the next restart).
/// </summary>
public sealed class LeagueAutoReconnectHostedService : BackgroundService
{
    private readonly ILeagueService _leagueService;
    private readonly ILeagueUserTeamStore _userTeamStore;
    private readonly ILogger<LeagueAutoReconnectHostedService> _logger;

    public LeagueAutoReconnectHostedService(
        ILeagueService leagueService,
        ILeagueUserTeamStore userTeamStore,
        ILogger<LeagueAutoReconnectHostedService> logger)
    {
        _leagueService = leagueService;
        _userTeamStore = userTeamStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var externalIds = _userTeamStore.GetConnectedExternalLeagueIds();
        if (externalIds.Count == 0)
        {
            return;
        }

        foreach (var externalId in externalIds)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var result = await _leagueService
                    .ConnectSleeperLeagueAsync(externalId, stoppingToken)
                    .ConfigureAwait(false);

                if (result.Succeeded)
                {
                    _logger.LogInformation(
                        "Auto-reconnected Sleeper league {ExternalId} ({LeagueName}) on startup.",
                        externalId,
                        result.League?.Name);
                }
                else
                {
                    _logger.LogWarning(
                        "Auto-reconnect failed for Sleeper league {ExternalId} on startup: {Error}",
                        externalId,
                        result.Error);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Auto-reconnect threw for Sleeper league {ExternalId} on startup.", externalId);
            }
        }
    }
}
