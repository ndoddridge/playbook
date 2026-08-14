using Microsoft.Extensions.Logging;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Research;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Core.Research;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Research;

/// <summary>
/// Postgame reconciliation pass. Grades snapshots whose event has concluded (commence time plus a
/// buffer) against real actual production, then persists the result via
/// <see cref="IPredictionResearchStore"/>. Player-level markets only (Passing/Rushing/Receiving
/// Yards, Receptions, Passing/Anytime Touchdowns) — game-level markets (Winner/Spread/Totals) have
/// no wired actual-outcome source yet and are graded as DataGap rather than guessed at.
///
/// Regular/postseason snapshots are graded from the existing REG/POST game-log store
/// (<see cref="IPlayerGameLogStore"/>) exactly as before. Preseason snapshots are graded from a
/// separate real source (<see cref="IPreseasonPlayerGameLogProvider"/>, ESPN boxscores) — nflverse's
/// player_stats feed has no preseason rows at all, and this separation also guarantees preseason
/// production can never leak into the regular-season game-log store that Projection consumes.
/// </summary>
public sealed class PostEventReconciliationService : IPostEventReconciliationService
{
    private static readonly TimeSpan GradingBuffer = TimeSpan.FromHours(5);

    private readonly IPredictionResearchStore _store;
    private readonly IPlayerGameLogStore _gameLogs;
    private readonly IPreseasonPlayerGameLogProvider _preseasonGameLogs;
    private readonly IPlayerInjuryService _injuries;
    private readonly IPredictionOutcomeClassifier _classifier;
    private readonly ILogger<PostEventReconciliationService> _logger;

    public PostEventReconciliationService(
        IPredictionResearchStore store,
        IPlayerGameLogStore gameLogs,
        IPreseasonPlayerGameLogProvider preseasonGameLogs,
        IPlayerInjuryService injuries,
        IPredictionOutcomeClassifier classifier,
        ILogger<PostEventReconciliationService> logger)
    {
        _store = store;
        _gameLogs = gameLogs;
        _preseasonGameLogs = preseasonGameLogs;
        _injuries = injuries;
        _classifier = classifier;
        _logger = logger;
    }

    public int RunPendingReconciliation()
    {
        var pending = _store.GetSnapshotsPendingGrading(DateTimeOffset.UtcNow, GradingBuffer);
        var graded = 0;

        foreach (var snapshot in pending)
        {
            try
            {
                var actual = ResolveActualValue(snapshot);
                var injury = snapshot.PlayerId is { } playerId ? _injuries.GetCurrentInjury(playerId) : null;
                var assessment = _classifier.Classify(snapshot, actual, injury);
                _store.SaveAssessment(assessment);
                graded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Failed to grade prediction snapshot {SnapshotId}", snapshot.SnapshotId);
            }
        }

        if (graded > 0)
        {
            _logger.LogInformation("Postgame reconciliation graded {Count} prediction snapshot(s).", graded);
        }

        return graded;
    }

    private decimal? ResolveActualValue(PredictionSnapshot snapshot)
    {
        if (snapshot.PlayerId is not { } playerId)
        {
            // Game-level markets (Winner/Spread/Totals) have no wired actual-outcome source.
            return null;
        }

        // Preseason has its own real source (ESPN) — nflverse's REG/POST-only game-log store has
        // nothing for these weeks, and must never be asked to answer for a preseason snapshot.
        if (snapshot.SeasonPhase == NflSeasonPhase.Preseason)
        {
            var preseasonLog = _preseasonGameLogs
                .GetPreseasonGameLogsAsync(snapshot.Season, snapshot.CommenceTime)
                .GetAwaiter()
                .GetResult()
                .FirstOrDefault(g => g.PlayerId == playerId);

            return preseasonLog is null ? null : ExtractMarketValue(snapshot.Market, preseasonLog);
        }

        var log = _gameLogs.GetGameLogsForPlayer(playerId)
            .FirstOrDefault(g => g.Season == snapshot.Season && g.Week == snapshot.Week);

        return log is null ? null : ExtractMarketValue(snapshot.Market, log);
    }

    private static decimal? ExtractMarketValue(PredictionMarketType market, PlayerGameStats log) =>
        market switch
        {
            PredictionMarketType.PassingYards => log.PassYards,
            PredictionMarketType.RushingYards => log.RushYards,
            PredictionMarketType.ReceivingYards => log.ReceivingYards,
            PredictionMarketType.Receptions => log.Receptions,
            PredictionMarketType.PassingTouchdowns => log.PassTouchdowns,
            PredictionMarketType.AnytimeTouchdown => AnytimeTouchdowns(log),
            _ => null
        };

    private static decimal? AnytimeTouchdowns(PlayerGameStats log)
    {
        if (log.RushTouchdowns is null && log.ReceivingTouchdowns is null)
        {
            return null;
        }

        return (log.RushTouchdowns ?? 0) + (log.ReceivingTouchdowns ?? 0);
    }
}
