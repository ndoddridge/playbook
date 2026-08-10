using Playbook.Core.Predictions;

namespace Playbook.Tests;

public class NflTeamCatalogTests
{
    [Theory]
    [InlineData("Bengals", "CIN")]
    [InlineData("Cincinnati", "CIN")]
    [InlineData("CIN", "CIN")]
    [InlineData("cincy", "CIN")]
    [InlineData("49ers", "SF")]
    [InlineData("San Francisco", "SF")]
    [InlineData("SF", "SF")]
    [InlineData("Niners", "SF")]
    [InlineData("Chiefs", "KC")]
    [InlineData("Kansas City", "KC")]
    [InlineData("KC", "KC")]
    [InlineData("Bills", "BUF")]
    [InlineData("Buffalo", "BUF")]
    public void ResolveAbbreviations_Maps_Common_Aliases(string query, string expected)
    {
        var resolved = NflTeamCatalog.ResolveAbbreviations(query);
        Assert.Contains(expected, resolved);
    }

    [Fact]
    public void Catalog_Has_All_Thirty_Two_Teams()
    {
        Assert.Equal(32, NflTeamCatalog.Teams.Count);
        Assert.Equal(32, NflTeamCatalog.Teams.Select(t => t.Abbreviation).Distinct().Count());
    }

    [Fact]
    public void QuickPickSearch_Bengals_Matches_CIN_Props()
    {
        var cin = MakePrediction("CIN", "CLE", "CIN", "Ja'Marr Chase");
        var other = MakePrediction("BUF", "BUF", "NYJ", "Josh Allen");

        Assert.True(QuickPickSearch.Matches(cin, "Bengals"));
        Assert.True(QuickPickSearch.Matches(cin, "Cincinnati"));
        Assert.True(QuickPickSearch.Matches(cin, "CIN"));
        Assert.False(QuickPickSearch.Matches(other, "Bengals"));
        Assert.True(QuickPickSearch.Matches(cin, "Chase"));
        Assert.True(QuickPickSearch.Matches(cin, "CLE @ CIN"));
    }

    [Fact]
    public void RankEligible_Orders_By_Opportunity_Without_Confidence_Floor()
    {
        var weak = MakePrediction("CIN", "CLE", "CIN", "A", confidence: 20, score: 2.0m);
        var strong = MakePrediction("CIN", "CLE", "CIN", "B", confidence: 30, score: 5.0m);
        var mid = MakePrediction("BUF", "BUF", "NYJ", "C", confidence: 80, score: 3.0m);

        var ranked = QuickPickSearch.RankEligible([weak, strong, mid]);
        Assert.Equal(3, ranked.Count);
        Assert.Equal("B", ranked[0].PlayerName);
        Assert.Equal("C", ranked[1].PlayerName);
        Assert.Equal("A", ranked[2].PlayerName);

        var bengals = QuickPickSearch.RankEligible(
            [weak, strong, mid],
            p => QuickPickSearch.Matches(p, "Bengals"));
        Assert.Equal(2, bengals.Count);
        Assert.All(bengals, p => Assert.Equal("CIN", p.TeamName));
        Assert.Equal("B", bengals[0].PlayerName);
    }

    private static Prediction MakePrediction(
        string team,
        string away,
        string home,
        string player,
        int confidence = 40,
        decimal score = 1m) =>
        new()
        {
            Id = Guid.NewGuid(),
            Event = new FootballEvent
            {
                EventId = $"{away}-{home}",
                AwayTeam = away,
                HomeTeam = home,
                CommenceTime = DateTimeOffset.UtcNow.AddDays(1),
                Season = 2026,
                Phase = NflSeasonPhase.Preseason,
                Week = 1
            },
            PlayerName = player,
            TeamName = team,
            Market = PredictionMarketType.ReceivingYards,
            Line = 70,
            PlaybookProjection = 80,
            Probability = 55,
            Edge = 2,
            Confidence = confidence,
            Direction = PredictionDirection.Over,
            Reasoning = "test",
            SupportingIntelligence = [],
            CalculationNotes = [],
            Source = "Mock",
            LineFreshness = PropLineFreshness.Mock,
            LastUpdated = DateTimeOffset.UtcNow,
            OpportunityScore = score
        };
}
