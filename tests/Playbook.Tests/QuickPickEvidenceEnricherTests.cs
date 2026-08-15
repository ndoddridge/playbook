using Playbook.Core.Predictions;
using Playbook.Core.Research;
using Playbook.Infrastructure.Predictions;

namespace Playbook.Tests;

public class QuickPickEvidenceEnricherTests
{
    [Fact]
    public void No_Evidence_Leaves_Prediction_Completely_Unchanged()
    {
        var prediction = MakePrediction();
        var empty = new PlayerEvidenceSummary { PlayerId = Guid.NewGuid(), Items = [], Headline = null };

        var result = QuickPickEvidenceEnricher.Apply(prediction, empty);

        Assert.Same(prediction.SupportingIntelligence, result.SupportingIntelligence);
        Assert.Equal(prediction.Confidence, result.Confidence);
        Assert.Equal(prediction.Edge, result.Edge);
        Assert.Equal(prediction.Probability, result.Probability);
        Assert.Equal(prediction.OpportunityScore, result.OpportunityScore);
    }

    [Fact]
    public void Weak_Isolated_Preseason_Evidence_Does_Not_Surface()
    {
        // A single preseason item is deliberately low-weight (SharedEvidenceService applies a
        // 0.5x phase discount on top of PreseasonNoise's already-low 0.2 base) — never treated
        // as strong evidence, so the pick stays exactly as it would without it.
        var prediction = MakePrediction();
        var weakItem = MakeItem(EvidenceType.PhaseNoise, weight: 0.1, phase: NflSeasonPhase.Preseason);
        var summary = new PlayerEvidenceSummary
        {
            PlayerId = Guid.NewGuid(),
            Items = [weakItem],
            Headline = weakItem.Summary
        };

        var result = QuickPickEvidenceEnricher.Apply(prediction, summary);

        Assert.Equal(prediction.SupportingIntelligence.Count, result.SupportingIntelligence.Count);
    }

    [Fact]
    public void Meaningful_Evidence_Adds_Exactly_One_Line_And_Nothing_Else_Changes()
    {
        var prediction = MakePrediction();
        var item = MakeItem(EvidenceType.MeaningfulRoleChange, weight: 0.6, phase: NflSeasonPhase.RegularSeason);
        var summary = new PlayerEvidenceSummary
        {
            PlayerId = Guid.NewGuid(),
            Items = [item],
            Headline = item.Summary
        };

        var result = QuickPickEvidenceEnricher.Apply(prediction, summary);

        Assert.Equal(prediction.SupportingIntelligence.Count + 1, result.SupportingIntelligence.Count);
        Assert.Contains(result.SupportingIntelligence, s => s.Contains(item.Summary, StringComparison.Ordinal));
        Assert.Contains("role change", result.SupportingIntelligence[^1], StringComparison.OrdinalIgnoreCase);

        // Never touches the actual score/ranking inputs.
        Assert.Equal(prediction.Confidence, result.Confidence);
        Assert.Equal(prediction.Edge, result.Edge);
        Assert.Equal(prediction.Probability, result.Probability);
        Assert.Equal(prediction.OpportunityScore, result.OpportunityScore);
        Assert.Equal(prediction.Direction, result.Direction);
        Assert.Equal(prediction.Reasoning, result.Reasoning);
    }

    [Fact]
    public void Repeated_Corroborating_Evidence_Is_Called_Out_As_Confirmed()
    {
        var prediction = MakePrediction();
        var first = MakeItem(EvidenceType.ProjectionAccuracy, weight: 0.55, phase: NflSeasonPhase.RegularSeason);
        var second = MakeItem(EvidenceType.ProjectionAccuracy, weight: 0.5, phase: NflSeasonPhase.RegularSeason);
        var third = MakeItem(EvidenceType.InjurySignal, weight: 0.4, phase: NflSeasonPhase.RegularSeason);
        var summary = new PlayerEvidenceSummary
        {
            PlayerId = Guid.NewGuid(),
            Items = [first, second, third],
            Headline = first.Summary
        };

        var result = QuickPickEvidenceEnricher.Apply(prediction, summary);

        var added = result.SupportingIntelligence[^1];
        Assert.Contains("confirmed across 2", added, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Single_Item_Evidence_Does_Not_Claim_Corroboration()
    {
        var prediction = MakePrediction();
        var item = MakeItem(EvidenceType.ProjectionError, weight: 0.5, phase: NflSeasonPhase.RegularSeason);
        var summary = new PlayerEvidenceSummary
        {
            PlayerId = Guid.NewGuid(),
            Items = [item],
            Headline = item.Summary
        };

        var result = QuickPickEvidenceEnricher.Apply(prediction, summary);

        Assert.DoesNotContain("confirmed across", result.SupportingIntelligence[^1], StringComparison.OrdinalIgnoreCase);
    }

    private static PlayerEvidenceItem MakeItem(EvidenceType type, double weight, NflSeasonPhase phase) => new()
    {
        SnapshotId = Guid.NewGuid(),
        PlayerId = Guid.NewGuid(),
        PlayerName = "Test Player",
        Type = type,
        Phase = phase,
        Season = 2026,
        Week = 1,
        Market = PredictionMarketType.ReceivingYards,
        Summary = "Wk 1: ReceivingYards actual 62 vs projection 45.",
        Weight = weight,
        ObservedAt = DateTimeOffset.UtcNow.AddDays(-2),
        Source = "Quick Picks research memory"
    };

    private static Prediction MakePrediction() => new()
    {
        Id = Guid.NewGuid(),
        Event = new FootballEvent
        {
            EventId = "test-cin-cle",
            HomeTeam = "CLE",
            AwayTeam = "CIN",
            CommenceTime = DateTimeOffset.UtcNow.AddDays(1),
            Season = 2026,
            Phase = NflSeasonPhase.RegularSeason,
            Week = 1
        },
        PlayerId = Guid.NewGuid(),
        PlayerName = "Ja'Marr Chase",
        TeamName = "CIN",
        Market = PredictionMarketType.ReceivingYards,
        Line = 94.5m,
        PlaybookProjection = 108.2m,
        Probability = 62,
        Edge = 13.7m,
        Confidence = 70,
        Direction = PredictionDirection.Over,
        Reasoning = "test reasoning",
        SupportingIntelligence = ["Health score 75."],
        CalculationNotes = ["test calc"],
        Source = "TheOddsAPI",
        LineFreshness = PropLineFreshness.Live,
        LastUpdated = DateTimeOffset.UtcNow,
        LineUpdatedAt = DateTimeOffset.UtcNow,
        Bookmaker = "williamhill_us",
        EngineVersion = "0.3",
        OpportunityScore = 8.5m
    };
}
