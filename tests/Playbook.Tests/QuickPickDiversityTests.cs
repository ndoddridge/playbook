using Playbook.Core.Predictions;

namespace Playbook.Tests;

public class QuickPickDiversityTests
{
    [Fact]
    public void SelectTop_Dedupes_Alternate_Spread_Lines_For_Same_Team()
    {
        var atl55 = TeamPick("ATL", "DEN", "ATL", PredictionMarketType.Spread, PredictionDirection.NotCover, 5.5m, score: 10);
        var atl45 = TeamPick("ATL", "DEN", "ATL", PredictionMarketType.Spread, PredictionDirection.NotCover, 4.5m, score: 9.5m);
        var chase = PlayerPick("Ja'Marr Chase", "CIN", "CLE", "CIN", PredictionMarketType.ReceivingYards, 94.5m, score: 8);
        var allen = PlayerPick("Josh Allen", "BUF", "BUF", "NYJ", PredictionMarketType.PassingYards, 265.5m, score: 7);
        var total = TeamPick(null, "SF", "LAR", PredictionMarketType.GameTotal, PredictionDirection.Over, 44.5m, score: 6);

        var selected = QuickPickDiversity.SelectTop([atl55, atl45, chase, allen, total], count: 5);

        Assert.Equal(4, selected.Count);
        Assert.Contains(selected, p => p.Id == atl55.Id);
        Assert.DoesNotContain(selected, p => p.Id == atl45.Id);
        Assert.Contains(selected, p => p.PlayerName == "Ja'Marr Chase");
        Assert.Contains(selected, p => p.PlayerName == "Josh Allen");
        Assert.Contains(selected, p => p.Market == PredictionMarketType.GameTotal);
    }

    [Fact]
    public void SelectTop_Dedupes_Alternate_Player_Lines()
    {
        var high = PlayerPick("Ja'Marr Chase", "CIN", "CLE", "CIN", PredictionMarketType.ReceivingYards, 94.5m, score: 10);
        var low = PlayerPick("Ja'Marr Chase", "CIN", "CLE", "CIN", PredictionMarketType.ReceivingYards, 89.5m, score: 9.8m);
        var other = PlayerPick("Tee Higgins", "CIN", "CLE", "CIN", PredictionMarketType.ReceivingYards, 70.5m, score: 7);

        var selected = QuickPickDiversity.SelectTop([high, low, other], count: 5);

        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, p => p.Id == high.Id);
        Assert.DoesNotContain(selected, p => p.Id == low.Id);
        Assert.Contains(selected, p => p.PlayerName == "Tee Higgins");
    }

    [Fact]
    public void SelectTop_Prefers_Stronger_Distinct_Over_Weaker()
    {
        var strong = PlayerPick("A", "BUF", "BUF", "MIA", PredictionMarketType.PassingYards, 250m, score: 12);
        var mid = PlayerPick("B", "KC", "KC", "DEN", PredictionMarketType.RushingYards, 80m, score: 8);
        var weak = PlayerPick("C", "SF", "SF", "SEA", PredictionMarketType.ReceivingYards, 60m, score: 3);

        var selected = QuickPickDiversity.SelectTop([strong, mid, weak], count: 2);
        Assert.Equal(new[] { strong.Id, mid.Id }, selected.Select(p => p.Id));
    }

    [Fact]
    public void SelectTop_Expands_When_Count_Increases()
    {
        var picks = Enumerable.Range(0, 6)
            .Select(i => PlayerPick($"P{i}", "T" + i, "T" + i, "X", PredictionMarketType.ReceivingYards, 50 + i, score: 10 - i))
            .ToList();

        var two = QuickPickDiversity.SelectTop(picks, 2);
        var five = QuickPickDiversity.SelectTop(picks, 5);

        Assert.Equal(2, two.Count);
        Assert.Equal(5, five.Count);
        Assert.All(two, p => Assert.Contains(p.Id, five.Select(x => x.Id)));
    }

    [Fact]
    public void Similarity_Marks_Alternate_Spreads_As_Near_Duplicates()
    {
        var a = TeamPick("ATL", "DEN", "ATL", PredictionMarketType.Spread, PredictionDirection.NotCover, 5.5m, 10);
        var b = TeamPick("ATL", "DEN", "ATL", PredictionMarketType.Spread, PredictionDirection.NotCover, 4.5m, 9);
        Assert.True(QuickPickDiversity.Similarity(a, b) >= QuickPickDiversity.NearDuplicateThreshold);
        Assert.Equal(QuickPickDiversity.OpportunityKey(a), QuickPickDiversity.OpportunityKey(b));
    }

    private static Prediction TeamPick(
        string? team,
        string away,
        string home,
        PredictionMarketType market,
        PredictionDirection direction,
        decimal line,
        decimal score) =>
        new()
        {
            Id = Guid.NewGuid(),
            Event = Ev(away, home),
            TeamName = team,
            Market = market,
            Line = line,
            PlaybookProjection = line - 2,
            Probability = 55,
            Edge = 2,
            Confidence = 40,
            Direction = direction,
            Reasoning = "t",
            SupportingIntelligence = [],
            CalculationNotes = [],
            Source = "Mock",
            LineFreshness = PropLineFreshness.Mock,
            LastUpdated = DateTimeOffset.UtcNow,
            OpportunityScore = score
        };

    private static Prediction PlayerPick(
        string player,
        string team,
        string away,
        string home,
        PredictionMarketType market,
        decimal line,
        decimal score) =>
        new()
        {
            Id = Guid.NewGuid(),
            Event = Ev(away, home),
            PlayerName = player,
            TeamName = team,
            Market = market,
            Line = line,
            PlaybookProjection = line + 10,
            Probability = 60,
            Edge = 5,
            Confidence = 50,
            Direction = PredictionDirection.Over,
            Reasoning = "t",
            SupportingIntelligence = [],
            CalculationNotes = [],
            Source = "Mock",
            LineFreshness = PropLineFreshness.Mock,
            LastUpdated = DateTimeOffset.UtcNow,
            OpportunityScore = score
        };

    private static FootballEvent Ev(string away, string home) => new()
    {
        EventId = $"{away}@{home}",
        AwayTeam = away,
        HomeTeam = home,
        CommenceTime = DateTimeOffset.UtcNow.AddDays(1),
        Season = 2026,
        Phase = NflSeasonPhase.Preseason,
        Week = 1
    };
}
