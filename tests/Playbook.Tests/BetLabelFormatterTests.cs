using Playbook.Core.Predictions;

namespace Playbook.Tests;

public class BetLabelFormatterTests
{
    [Fact]
    public void Spread_Cover_Names_The_Home_Team_With_Its_Signed_Line()
    {
        var badge = BetLabelFormatter.FormatBadge(
            PredictionMarketType.Spread, PredictionDirection.Cover, -3.5m, "Chiefs", "Chiefs", "Bills");

        Assert.Equal("BET: Chiefs -3.5", badge);
    }

    [Fact]
    public void Spread_NotCover_Names_The_Other_Team_With_The_Inverted_Sign()
    {
        // Home team (Chiefs) quoted at -3.5; "not cover" means the away team (Bills) at +3.5 —
        // never a bare "Not Cover 3.5" that leaves the reader to work out who and which sign.
        var badge = BetLabelFormatter.FormatBadge(
            PredictionMarketType.Spread, PredictionDirection.NotCover, -3.5m, "Chiefs", "Chiefs", "Bills");

        Assert.Equal("BET: Bills +3.5", badge);
    }

    [Fact]
    public void Spread_NotCover_When_Home_Team_Is_The_Underdog_Names_Home_Team_Favored()
    {
        var badge = BetLabelFormatter.FormatBadge(
            PredictionMarketType.Spread, PredictionDirection.NotCover, 3.5m, "Bills", "Bills", "Chiefs");

        Assert.Equal("BET: Chiefs -3.5", badge);
    }

    [Fact]
    public void GameTotal_Over_States_Bet_Over_With_Line()
    {
        var badge = BetLabelFormatter.FormatBadge(
            PredictionMarketType.GameTotal, PredictionDirection.Over, 36.5m, null, "Chiefs", "Bills");

        Assert.Equal("BET OVER 36.5", badge);
    }

    [Fact]
    public void GameTotal_Under_States_Bet_Under_With_Line()
    {
        var badge = BetLabelFormatter.FormatBadge(
            PredictionMarketType.GameTotal, PredictionDirection.Under, 36.5m, null, "Chiefs", "Bills");

        Assert.Equal("BET UNDER 36.5", badge);
    }

    [Fact]
    public void TeamTotal_Includes_The_Team_Name()
    {
        var badge = BetLabelFormatter.FormatBadge(
            PredictionMarketType.TeamTotal, PredictionDirection.Over, 24.5m, "Chiefs", "Chiefs", "Bills");

        Assert.Equal("BET Chiefs OVER 24.5", badge);
    }

    [Fact]
    public void Player_Prop_Markets_Are_Unchanged_No_Bet_Prefix()
    {
        var badge = BetLabelFormatter.FormatBadge(
            PredictionMarketType.ReceivingYards, PredictionDirection.Over, 65.5m, null, "Chiefs", "Bills");

        Assert.Equal("OVER 65.5", badge);
    }
}
