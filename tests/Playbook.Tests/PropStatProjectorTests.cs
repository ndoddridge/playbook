using Playbook.Core.Predictions;
using Playbook.Infrastructure.Predictions;

namespace Playbook.Tests;

/// <summary>
/// Game markets (Spread/Total/Winner/TeamTotal) have no real team/game projection input in this
/// version of Playbook (IMatchupContextProvider / IGameEnvironmentProvider are both
/// "Unavailable"). PropStatProjector must report that honestly (null projection) instead of
/// fabricating a fixed placeholder number that looks like real intelligence — for every season
/// phase, since neither preseason nor regular season has a legitimate signal behind it.
/// </summary>
public class PropStatProjectorTests
{
    [Theory]
    [InlineData(PredictionMarketType.GameTotal)]
    [InlineData(PredictionMarketType.TeamTotal)]
    [InlineData(PredictionMarketType.Winner)]
    [InlineData(PredictionMarketType.Spread)]
    public void Preseason_Game_Markets_Report_No_Projection(PredictionMarketType market)
    {
        var (projection, _, _, usingPrior) = PropStatProjector.Project(
            market, production: null, stats: null, intelligence: null, NflSeasonPhase.Preseason);

        Assert.Null(projection);
        Assert.False(usingPrior);
    }

    [Theory]
    [InlineData(PredictionMarketType.GameTotal)]
    [InlineData(PredictionMarketType.TeamTotal)]
    [InlineData(PredictionMarketType.Winner)]
    [InlineData(PredictionMarketType.Spread)]
    public void Regular_Season_Game_Markets_Report_No_Projection(PredictionMarketType market)
    {
        var (projection, _, _, usingPrior) = PropStatProjector.Project(
            market, production: null, stats: null, intelligence: null, NflSeasonPhase.RegularSeason);

        Assert.Null(projection);
        Assert.False(usingPrior);
    }
}
