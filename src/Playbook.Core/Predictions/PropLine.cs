namespace Playbook.Core.Predictions;

/// <summary>
/// Normalized market line from a prop provider (book/source).
/// </summary>
public sealed class PropLine
{
    public required string Id { get; init; }

    public required FootballEvent Event { get; init; }

    public Guid? PlayerId { get; init; }

    public string? PlayerName { get; init; }

    public string? TeamName { get; init; }

    public required PredictionMarketType Market { get; init; }

    /// <summary>Numeric line when applicable (yards, receptions, total, spread point).</summary>
    public decimal? Line { get; init; }

    public required string Bookmaker { get; init; }

    public required string Source { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required PropLineFreshness Freshness { get; init; }

    public int? AmericanOddsOver { get; init; }

    public int? AmericanOddsUnder { get; init; }

    public string MarketLabel => Market switch
    {
        PredictionMarketType.PassingYards => "Passing Yards",
        PredictionMarketType.RushingYards => "Rushing Yards",
        PredictionMarketType.ReceivingYards => "Receiving Yards",
        PredictionMarketType.Receptions => "Receptions",
        PredictionMarketType.AnytimeTouchdown => "Anytime Touchdown",
        PredictionMarketType.PassingTouchdowns => "Passing Touchdowns",
        PredictionMarketType.TeamTotal => "Team Total",
        PredictionMarketType.GameTotal => "Game Total",
        PredictionMarketType.Winner => "Moneyline",
        PredictionMarketType.Spread => "Spread",
        _ => Market.ToString()
    };
}
