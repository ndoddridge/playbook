using Playbook.Core.Injuries.Models;
using Playbook.Core.Predictions;
using Playbook.Core.Research;
using Playbook.Infrastructure.Research;

namespace Playbook.Tests;

public class PredictionOutcomeClassifierTests
{
    private readonly PredictionOutcomeClassifier _classifier = new();

    [Fact]
    public void Missing_Actual_Value_Is_DataGap()
    {
        var snapshot = MakeSnapshot(NflSeasonPhase.RegularSeason, projection: 60m, line: 55.5m, direction: PredictionDirection.Over);

        var result = _classifier.Classify(snapshot, actualValue: null, injuryAtGradingTime: null);

        Assert.Equal(PredictionOutcomeClassification.DataGap, result.Classification);
        Assert.Null(result.ActualValue);
        Assert.Null(result.DirectionHit);
    }

    [Fact]
    public void Actual_Close_To_Projection_Is_Success()
    {
        var snapshot = MakeSnapshot(NflSeasonPhase.RegularSeason, projection: 60m, line: 55.5m, direction: PredictionDirection.Over);

        var result = _classifier.Classify(snapshot, actualValue: 62m, injuryAtGradingTime: null);

        Assert.Equal(PredictionOutcomeClassification.Success, result.Classification);
        Assert.True(result.DirectionHit);
    }

    [Fact]
    public void Big_Miss_With_Significant_Injury_On_File_Is_InjurySignal()
    {
        var snapshot = MakeSnapshot(NflSeasonPhase.RegularSeason, projection: 70m, line: 65.5m, direction: PredictionDirection.Over);
        var injury = new PlayerInjuryRecord
        {
            PlayerId = snapshot.PlayerId!.Value,
            Date = DateTimeOffset.UtcNow,
            Status = "Out",
            Severity = InjurySeverity.Significant,
            IsCurrent = true
        };

        var result = _classifier.Classify(snapshot, actualValue: 10m, injuryAtGradingTime: injury);

        Assert.Equal(PredictionOutcomeClassification.InjurySignal, result.Classification);
        Assert.False(result.DirectionHit);
    }

    [Fact]
    public void Actual_Far_Above_Projection_Is_MeaningfulRoleSignal()
    {
        var snapshot = MakeSnapshot(NflSeasonPhase.RegularSeason, projection: 20m, line: 25.5m, direction: PredictionDirection.Over);

        var result = _classifier.Classify(snapshot, actualValue: 60m, injuryAtGradingTime: null);

        Assert.Equal(PredictionOutcomeClassification.MeaningfulRoleSignal, result.Classification);
    }

    [Fact]
    public void Actual_Far_Below_Projection_With_No_Injury_Is_RoleError()
    {
        var snapshot = MakeSnapshot(NflSeasonPhase.RegularSeason, projection: 60m, line: 55.5m, direction: PredictionDirection.Over);

        var result = _classifier.Classify(snapshot, actualValue: 5m, injuryAtGradingTime: null);

        Assert.Equal(PredictionOutcomeClassification.RoleError, result.Classification);
    }

    [Fact]
    public void Moderate_Preseason_Miss_Is_PreseasonNoise()
    {
        var snapshot = MakeSnapshot(NflSeasonPhase.Preseason, projection: 20m, line: 18.5m, direction: PredictionDirection.Over);

        var result = _classifier.Classify(snapshot, actualValue: 12m, injuryAtGradingTime: null);

        Assert.Equal(PredictionOutcomeClassification.PreseasonNoise, result.Classification);
    }

    [Fact]
    public void Moderate_RegularSeason_Miss_Beyond_Tight_Tolerance_Is_RegularSeasonNoise()
    {
        var snapshot = MakeSnapshot(NflSeasonPhase.RegularSeason, projection: 60m, line: 55.5m, direction: PredictionDirection.Over);

        // ~28% below projection: outside the tight (20%) tolerance but inside the wider noise band,
        // and short of the 50% "big miss" threshold that would trigger RoleError.
        var result = _classifier.Classify(snapshot, actualValue: 43m, injuryAtGradingTime: null);

        Assert.Equal(PredictionOutcomeClassification.RegularSeasonNoise, result.Classification);
    }

    [Fact]
    public void Never_Overwrites_Classification_Fields_Beyond_What_Was_Graded()
    {
        // Sanity: SnapshotId always round-trips so the assessment can always be linked back.
        var snapshot = MakeSnapshot(NflSeasonPhase.RegularSeason, projection: 10m, line: 9.5m, direction: PredictionDirection.Over);

        var result = _classifier.Classify(snapshot, actualValue: 11m, injuryAtGradingTime: null);

        Assert.Equal(snapshot.SnapshotId, result.SnapshotId);
    }

    private static PredictionSnapshot MakeSnapshot(
        NflSeasonPhase phase, decimal projection, decimal line, PredictionDirection direction) => new()
    {
        SnapshotId = Guid.NewGuid(),
        PlayerId = Guid.NewGuid(),
        PlayerName = "Test Player",
        EventId = "evt-1",
        CommenceTime = DateTimeOffset.UtcNow.AddDays(-1),
        Season = 2026,
        Week = 1,
        SeasonPhase = phase,
        Market = PredictionMarketType.ReceivingYards,
        Direction = direction,
        Line = line,
        Bookmaker = "williamhill_us",
        LineSource = "TheOddsAPI",
        LineUpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        PlaybookProjection = projection,
        Probability = 55,
        Confidence = 60,
        Reasoning = "test",
        SupportingIntelligence = [],
        CapturedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };
}
