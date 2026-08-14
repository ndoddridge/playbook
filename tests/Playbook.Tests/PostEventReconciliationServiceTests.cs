using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Predictions;
using Playbook.Core.Research;
using Playbook.Core.Stats.Models;
using Playbook.Infrastructure.Research;

namespace Playbook.Tests;

public class PostEventReconciliationServiceTests : IDisposable
{
    private readonly string _root;

    public PostEventReconciliationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "playbook-reconciliation-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Grades_A_Due_Snapshot_Using_The_Matching_GameLog_Stat()
    {
        var playerId = Guid.NewGuid();
        var store = new PredictionResearchStore(NullLogger<PredictionResearchStore>.Instance, _root);
        var snapshot = MakeSnapshot(playerId, commenceTime: DateTimeOffset.UtcNow.AddHours(-10));
        store.SaveSnapshot(snapshot);

        var gameLogs = new FakeGameLogStore(
        [
            new PlayerGameStats
            {
                PlayerId = playerId,
                Season = snapshot.Season,
                Week = snapshot.Week,
                SeasonType = "REG",
                ReceivingYards = 62
            }
        ]);
        var service = new PostEventReconciliationService(
            store, gameLogs, new FakeInjuryService(null), new PredictionOutcomeClassifier(),
            NullLogger<PostEventReconciliationService>.Instance);

        var graded = service.RunPendingReconciliation();

        Assert.Equal(1, graded);
        var assessment = Assert.Single(store.GetAllAssessments());
        Assert.Equal(62m, assessment.ActualValue);
        Assert.Equal(PredictionOutcomeClassification.Success, assessment.Classification);
        Assert.True(store.HasAssessment(snapshot.SnapshotId));
    }

    [Fact]
    public void Missing_GameLog_Grades_As_DataGap_Not_Skipped()
    {
        var playerId = Guid.NewGuid();
        var store = new PredictionResearchStore(NullLogger<PredictionResearchStore>.Instance, _root);
        var snapshot = MakeSnapshot(playerId, commenceTime: DateTimeOffset.UtcNow.AddHours(-10));
        store.SaveSnapshot(snapshot);

        var service = new PostEventReconciliationService(
            store, new FakeGameLogStore([]), new FakeInjuryService(null), new PredictionOutcomeClassifier(),
            NullLogger<PostEventReconciliationService>.Instance);

        service.RunPendingReconciliation();

        var assessment = Assert.Single(store.GetAllAssessments());
        Assert.Equal(PredictionOutcomeClassification.DataGap, assessment.Classification);
    }

    [Fact]
    public void Snapshot_Not_Yet_Due_Is_Not_Graded()
    {
        var playerId = Guid.NewGuid();
        var store = new PredictionResearchStore(NullLogger<PredictionResearchStore>.Instance, _root);
        var snapshot = MakeSnapshot(playerId, commenceTime: DateTimeOffset.UtcNow.AddHours(-1));
        store.SaveSnapshot(snapshot);

        var service = new PostEventReconciliationService(
            store, new FakeGameLogStore([]), new FakeInjuryService(null), new PredictionOutcomeClassifier(),
            NullLogger<PostEventReconciliationService>.Instance);

        var graded = service.RunPendingReconciliation();

        Assert.Equal(0, graded);
        Assert.Empty(store.GetAllAssessments());
    }

    [Fact]
    public void Already_Graded_Snapshot_Is_Not_Graded_Again()
    {
        var playerId = Guid.NewGuid();
        var store = new PredictionResearchStore(NullLogger<PredictionResearchStore>.Instance, _root);
        var snapshot = MakeSnapshot(playerId, commenceTime: DateTimeOffset.UtcNow.AddHours(-10));
        store.SaveSnapshot(snapshot);
        var gameLogs = new FakeGameLogStore(
        [
            new PlayerGameStats
            {
                PlayerId = playerId, Season = snapshot.Season, Week = snapshot.Week,
                SeasonType = "REG", ReceivingYards = 62
            }
        ]);
        var service = new PostEventReconciliationService(
            store, gameLogs, new FakeInjuryService(null), new PredictionOutcomeClassifier(),
            NullLogger<PostEventReconciliationService>.Instance);

        var firstPass = service.RunPendingReconciliation();
        var secondPass = service.RunPendingReconciliation();

        Assert.Equal(1, firstPass);
        Assert.Equal(0, secondPass);
        Assert.Single(store.GetAllAssessments());
    }

    private static PredictionSnapshot MakeSnapshot(Guid playerId, DateTimeOffset commenceTime) => new()
    {
        SnapshotId = Guid.NewGuid(),
        PlayerId = playerId,
        PlayerName = "Test Player",
        EventId = "evt-1",
        CommenceTime = commenceTime,
        Season = 2026,
        Week = 1,
        SeasonPhase = NflSeasonPhase.RegularSeason,
        Market = PredictionMarketType.ReceivingYards,
        Direction = PredictionDirection.Over,
        Line = 55.5m,
        Bookmaker = "williamhill_us",
        LineSource = "TheOddsAPI",
        LineUpdatedAt = commenceTime,
        PlaybookProjection = 60m,
        Probability = 55,
        Confidence = 60,
        Reasoning = "test",
        SupportingIntelligence = [],
        CapturedAt = commenceTime
    };

    private sealed class FakeGameLogStore : IPlayerGameLogStore
    {
        private readonly IReadOnlyList<PlayerGameStats> _logs;
        public FakeGameLogStore(IReadOnlyList<PlayerGameStats> logs) => _logs = logs;
        public IReadOnlyList<PlayerGameStats> GetAllGameLogs() => _logs;
        public IReadOnlyList<PlayerGameStats> GetGameLogsForPlayer(Guid playerId) =>
            _logs.Where(l => l.PlayerId == playerId).ToList();
        public IReadOnlyList<PlayerGameStats> GetRecentGameLogs(Guid playerId, int maxGames = 8) =>
            GetGameLogsForPlayer(playerId).Take(maxGames).ToList();
        public int GameLogCount => _logs.Count;
    }

    private sealed class FakeInjuryService : IPlayerInjuryService
    {
        private readonly PlayerInjuryRecord? _current;
        public FakeInjuryService(PlayerInjuryRecord? current) => _current = current;
        public InjuryProviderCapabilities ActiveCapabilities => InjuryProviderCapabilities.MockCurrentOnly;
        public HistoricalDataStatus GlobalHistoricalDataStatus => HistoricalDataStatus.NotSupportedByProvider;
        public IReadOnlyList<PlayerInjuryRecord> GetAllInjuries() => _current is null ? [] : [_current];
        public IReadOnlyList<PlayerInjuryRecord> GetInjuriesForPlayer(Guid playerId) => GetAllInjuries();
        public PlayerInjuryRecord? GetCurrentInjury(Guid playerId) => _current;
        public IReadOnlyList<PlayerInjuryRecord> GetHistoricalInjuries(Guid playerId) => [];
        public PlayerInjuryProfile GetPlayerInjuryProfile(Guid playerId) => new() { PlayerId = playerId };
        public void Refresh()
        {
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
