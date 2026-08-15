using Playbook.Application.Research;
using Playbook.Core.Predictions;
using Playbook.Core.Research;
using Playbook.Infrastructure.Research;

namespace Playbook.Tests;

public class SharedEvidenceServiceTests
{
    [Fact]
    public void No_Snapshots_For_Player_Returns_Empty_Summary_Not_Fabricated()
    {
        var service = new SharedEvidenceService(new FakeStore([], []));

        var summary = service.GetEvidenceForPlayer(Guid.NewGuid());

        Assert.False(summary.HasEvidence);
        Assert.Empty(summary.Items);
        Assert.Null(summary.Headline);
    }

    [Fact]
    public void Ungraded_Snapshot_Produces_No_Evidence_Item()
    {
        var playerId = Guid.NewGuid();
        var snapshot = MakeSnapshot(playerId, week: 1, phase: NflSeasonPhase.RegularSeason, capturedDaysAgo: 3);
        var service = new SharedEvidenceService(new FakeStore([snapshot], []));

        var summary = service.GetEvidenceForPlayer(playerId);

        Assert.False(summary.HasEvidence);
    }

    [Theory]
    [InlineData(PredictionOutcomeClassification.Success, EvidenceType.ProjectionAccuracy)]
    [InlineData(PredictionOutcomeClassification.ProjectionError, EvidenceType.ProjectionError)]
    [InlineData(PredictionOutcomeClassification.RoleError, EvidenceType.RoleConcern)]
    [InlineData(PredictionOutcomeClassification.MeaningfulRoleSignal, EvidenceType.MeaningfulRoleChange)]
    [InlineData(PredictionOutcomeClassification.InjurySignal, EvidenceType.InjurySignal)]
    [InlineData(PredictionOutcomeClassification.DataGap, EvidenceType.ParticipationGap)]
    [InlineData(PredictionOutcomeClassification.PreseasonNoise, EvidenceType.PhaseNoise)]
    [InlineData(PredictionOutcomeClassification.RegularSeasonNoise, EvidenceType.PhaseNoise)]
    public void Classification_Maps_To_Expected_EvidenceType(
        PredictionOutcomeClassification classification, EvidenceType expected)
    {
        var playerId = Guid.NewGuid();
        var snapshot = MakeSnapshot(playerId, week: 1, phase: NflSeasonPhase.RegularSeason, capturedDaysAgo: 3);
        var assessment = MakeAssessment(snapshot.SnapshotId, classification, gradedDaysAgo: 2);
        var service = new SharedEvidenceService(new FakeStore([snapshot], [assessment]));

        var summary = service.GetEvidenceForPlayer(playerId);

        var item = Assert.Single(summary.Items);
        Assert.Equal(expected, item.Type);
    }

    [Fact]
    public void Preseason_Evidence_Weighs_Less_Than_Identical_RegularSeason_Evidence()
    {
        var preseasonPlayer = Guid.NewGuid();
        var regularPlayer = Guid.NewGuid();
        var preseasonSnapshot = MakeSnapshot(preseasonPlayer, week: 1, phase: NflSeasonPhase.Preseason, capturedDaysAgo: 3);
        var regularSnapshot = MakeSnapshot(regularPlayer, week: 1, phase: NflSeasonPhase.RegularSeason, capturedDaysAgo: 3);
        var preseasonAssessment = MakeAssessment(
            preseasonSnapshot.SnapshotId, PredictionOutcomeClassification.Success, gradedDaysAgo: 2);
        var regularAssessment = MakeAssessment(
            regularSnapshot.SnapshotId, PredictionOutcomeClassification.Success, gradedDaysAgo: 2);
        var service = new SharedEvidenceService(
            new FakeStore([preseasonSnapshot, regularSnapshot], [preseasonAssessment, regularAssessment]));

        var preseasonItem = Assert.Single(service.GetEvidenceForPlayer(preseasonPlayer).Items);
        var regularItem = Assert.Single(service.GetEvidenceForPlayer(regularPlayer).Items);

        Assert.True(preseasonItem.Weight < regularItem.Weight);
    }

    [Fact]
    public void Older_Evidence_Weighs_Less_Than_Newer_Evidence_Of_The_Same_Kind()
    {
        var recentPlayer = Guid.NewGuid();
        var oldPlayer = Guid.NewGuid();
        var recentSnapshot = MakeSnapshot(recentPlayer, week: 1, phase: NflSeasonPhase.RegularSeason, capturedDaysAgo: 5);
        var oldSnapshot = MakeSnapshot(oldPlayer, week: 1, phase: NflSeasonPhase.RegularSeason, capturedDaysAgo: 100);
        var recentAssessment = MakeAssessment(
            recentSnapshot.SnapshotId, PredictionOutcomeClassification.Success, gradedDaysAgo: 4);
        var oldAssessment = MakeAssessment(
            oldSnapshot.SnapshotId, PredictionOutcomeClassification.Success, gradedDaysAgo: 99);
        var service = new SharedEvidenceService(
            new FakeStore([recentSnapshot, oldSnapshot], [recentAssessment, oldAssessment]));

        var recentItem = Assert.Single(service.GetEvidenceForPlayer(recentPlayer).Items);
        var oldItem = Assert.Single(service.GetEvidenceForPlayer(oldPlayer).Items);

        Assert.True(oldItem.Weight < recentItem.Weight);
        // Decay fades but never fully erases the evidence.
        Assert.True(oldItem.Weight > 0);
    }

    [Fact]
    public void DataGap_Evidence_Weighs_Less_Than_A_Confirmed_Success_At_The_Same_Age()
    {
        var gapPlayer = Guid.NewGuid();
        var successPlayer = Guid.NewGuid();
        var gapSnapshot = MakeSnapshot(gapPlayer, week: 1, phase: NflSeasonPhase.RegularSeason, capturedDaysAgo: 3);
        var successSnapshot = MakeSnapshot(successPlayer, week: 1, phase: NflSeasonPhase.RegularSeason, capturedDaysAgo: 3);
        var gapAssessment = MakeAssessment(
            gapSnapshot.SnapshotId, PredictionOutcomeClassification.DataGap, gradedDaysAgo: 2);
        var successAssessment = MakeAssessment(
            successSnapshot.SnapshotId, PredictionOutcomeClassification.Success, gradedDaysAgo: 2);
        var service = new SharedEvidenceService(
            new FakeStore([gapSnapshot, successSnapshot], [gapAssessment, successAssessment]));

        var gapItem = Assert.Single(service.GetEvidenceForPlayer(gapPlayer).Items);
        var successItem = Assert.Single(service.GetEvidenceForPlayer(successPlayer).Items);

        Assert.True(gapItem.Weight < successItem.Weight);
    }

    [Fact]
    public void Headline_Is_The_Highest_Weight_Items_Summary()
    {
        var playerId = Guid.NewGuid();
        var weakSnapshot = MakeSnapshot(playerId, week: 1, phase: NflSeasonPhase.Preseason, capturedDaysAgo: 3);
        var strongSnapshot = MakeSnapshot(playerId, week: 2, phase: NflSeasonPhase.RegularSeason, capturedDaysAgo: 3);
        var weakAssessment = MakeAssessment(
            weakSnapshot.SnapshotId, PredictionOutcomeClassification.DataGap, gradedDaysAgo: 2);
        var strongAssessment = MakeAssessment(
            strongSnapshot.SnapshotId, PredictionOutcomeClassification.Success, gradedDaysAgo: 2);
        var service = new SharedEvidenceService(
            new FakeStore([weakSnapshot, strongSnapshot], [weakAssessment, strongAssessment]));

        var summary = service.GetEvidenceForPlayer(playerId);

        var strongItem = summary.Items.Single(i => i.Type == EvidenceType.ProjectionAccuracy);
        Assert.Equal(strongItem.Summary, summary.Headline);
    }

    private static PredictionSnapshot MakeSnapshot(
        Guid playerId, int week, NflSeasonPhase phase, int capturedDaysAgo) => new()
    {
        SnapshotId = Guid.NewGuid(),
        PlayerId = playerId,
        PlayerName = "Test Player",
        EventId = $"evt-{Guid.NewGuid():N}",
        CommenceTime = DateTimeOffset.UtcNow.AddDays(-capturedDaysAgo).AddHours(-3),
        Season = 2026,
        Week = week,
        SeasonPhase = phase,
        Market = PredictionMarketType.ReceivingYards,
        Direction = PredictionDirection.Over,
        Line = 55.5m,
        Bookmaker = "williamhill_us",
        LineSource = "TheOddsAPI",
        LineUpdatedAt = DateTimeOffset.UtcNow.AddDays(-capturedDaysAgo),
        PlaybookProjection = 60m,
        Probability = 55,
        Confidence = 60,
        Reasoning = "test",
        SupportingIntelligence = [],
        CapturedAt = DateTimeOffset.UtcNow.AddDays(-capturedDaysAgo)
    };

    private static PredictionOutcomeAssessment MakeAssessment(
        Guid snapshotId, PredictionOutcomeClassification classification, int gradedDaysAgo) => new()
    {
        SnapshotId = snapshotId,
        ActualValue = 62m,
        DirectionHit = true,
        ProjectionDelta = 2m,
        Classification = classification,
        AssessmentNotes = "test",
        GradedAt = DateTimeOffset.UtcNow.AddDays(-gradedDaysAgo)
    };

    private sealed class FakeStore(
        IReadOnlyList<PredictionSnapshot> snapshots,
        IReadOnlyList<PredictionOutcomeAssessment> assessments) : IPredictionResearchStore
    {
        public void SaveSnapshot(PredictionSnapshot snapshot) => throw new NotSupportedException();
        public IReadOnlyList<PredictionSnapshot> GetAllSnapshots() => snapshots;
        public IReadOnlyList<PredictionSnapshot> GetSnapshotsPendingGrading(
            DateTimeOffset asOf, TimeSpan gradingBuffer) => throw new NotSupportedException();
        public void SaveAssessment(PredictionOutcomeAssessment assessment) => throw new NotSupportedException();
        public IReadOnlyList<PredictionOutcomeAssessment> GetAllAssessments() => assessments;
        public bool HasAssessment(Guid snapshotId) => assessments.Any(a => a.SnapshotId == snapshotId);
    }
}
