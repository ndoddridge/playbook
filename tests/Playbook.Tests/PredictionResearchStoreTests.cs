using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Core.Predictions;
using Playbook.Core.Research;
using Playbook.Infrastructure.Research;

namespace Playbook.Tests;

public class PredictionResearchStoreTests : IDisposable
{
    private readonly string _root;

    public PredictionResearchStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "playbook-research-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Saved_Snapshot_Round_Trips()
    {
        var store = new PredictionResearchStore(NullLogger<PredictionResearchStore>.Instance, _root);
        var snapshot = MakeSnapshot();

        store.SaveSnapshot(snapshot);

        var all = store.GetAllSnapshots();
        var saved = Assert.Single(all);
        Assert.Equal(snapshot.SnapshotId, saved.SnapshotId);
        Assert.Equal(snapshot.PlayerName, saved.PlayerName);
    }

    [Fact]
    public void Saving_A_Snapshot_With_The_Same_Id_Twice_Never_Overwrites()
    {
        var store = new PredictionResearchStore(NullLogger<PredictionResearchStore>.Instance, _root);
        var snapshot = MakeSnapshot();
        store.SaveSnapshot(snapshot);

        store.SaveSnapshot(new PredictionSnapshot
        {
            SnapshotId = snapshot.SnapshotId,
            PlayerId = snapshot.PlayerId,
            PlayerName = "Tampered Name",
            EventId = snapshot.EventId,
            CommenceTime = snapshot.CommenceTime,
            Season = snapshot.Season,
            Week = snapshot.Week,
            SeasonPhase = snapshot.SeasonPhase,
            Market = snapshot.Market,
            Direction = snapshot.Direction,
            Line = snapshot.Line,
            Bookmaker = snapshot.Bookmaker,
            LineSource = snapshot.LineSource,
            LineUpdatedAt = snapshot.LineUpdatedAt,
            PlaybookProjection = snapshot.PlaybookProjection,
            Probability = snapshot.Probability,
            Confidence = snapshot.Confidence,
            Reasoning = snapshot.Reasoning,
            SupportingIntelligence = snapshot.SupportingIntelligence,
            CapturedAt = snapshot.CapturedAt
        });

        var all = store.GetAllSnapshots();
        var saved = Assert.Single(all);
        Assert.Equal(snapshot.PlayerName, saved.PlayerName);
        Assert.NotEqual("Tampered Name", saved.PlayerName);
    }

    [Fact]
    public void Saving_An_Assessment_With_The_Same_SnapshotId_Twice_Never_Overwrites()
    {
        var store = new PredictionResearchStore(NullLogger<PredictionResearchStore>.Instance, _root);
        var snapshotId = Guid.NewGuid();
        var first = MakeAssessment(snapshotId, PredictionOutcomeClassification.Success);
        store.SaveAssessment(first);

        store.SaveAssessment(MakeAssessment(snapshotId, PredictionOutcomeClassification.ProjectionError));

        var all = store.GetAllAssessments();
        var saved = Assert.Single(all);
        Assert.Equal(PredictionOutcomeClassification.Success, saved.Classification);
    }

    [Fact]
    public void Pending_Grading_Excludes_Already_Graded_And_Not_Yet_Due_Snapshots()
    {
        var store = new PredictionResearchStore(NullLogger<PredictionResearchStore>.Instance, _root);
        var now = DateTimeOffset.UtcNow;

        var due = MakeSnapshot(commenceTime: now.AddHours(-10));
        var notYetDue = MakeSnapshot(commenceTime: now.AddHours(-1));
        var alreadyGraded = MakeSnapshot(commenceTime: now.AddHours(-10));

        store.SaveSnapshot(due);
        store.SaveSnapshot(notYetDue);
        store.SaveSnapshot(alreadyGraded);
        store.SaveAssessment(MakeAssessment(alreadyGraded.SnapshotId, PredictionOutcomeClassification.Success));

        var pending = store.GetSnapshotsPendingGrading(now, TimeSpan.FromHours(5));

        var pendingId = Assert.Single(pending).SnapshotId;
        Assert.Equal(due.SnapshotId, pendingId);
    }

    private static PredictionSnapshot MakeSnapshot(DateTimeOffset? commenceTime = null) => new()
    {
        SnapshotId = Guid.NewGuid(),
        PlayerId = Guid.NewGuid(),
        PlayerName = "Original Name",
        EventId = "evt-1",
        CommenceTime = commenceTime ?? DateTimeOffset.UtcNow.AddDays(-1),
        Season = 2026,
        Week = 1,
        SeasonPhase = NflSeasonPhase.RegularSeason,
        Market = PredictionMarketType.ReceivingYards,
        Direction = PredictionDirection.Over,
        Line = 55.5m,
        Bookmaker = "williamhill_us",
        LineSource = "TheOddsAPI",
        LineUpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        PlaybookProjection = 60m,
        Probability = 55,
        Confidence = 60,
        Reasoning = "test",
        SupportingIntelligence = [],
        CapturedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };

    private static PredictionOutcomeAssessment MakeAssessment(
        Guid snapshotId, PredictionOutcomeClassification classification) => new()
    {
        SnapshotId = snapshotId,
        ActualValue = 60m,
        DirectionHit = true,
        ProjectionDelta = 0m,
        Classification = classification,
        AssessmentNotes = "test",
        GradedAt = DateTimeOffset.UtcNow
    };
}
