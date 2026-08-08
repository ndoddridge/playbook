namespace Playbook.Core.Predictions;

/// <summary>
/// Quick Picks prediction — football event outcome estimate.
/// Intentionally independent of fantasy league / roster / scoring context.
/// Extensible for same-game combos, distributions, and backtesting later.
/// </summary>
public sealed class Prediction
{
    public required Guid Id { get; init; }

    public required FootballEvent Event { get; init; }

    public Guid? PlayerId { get; init; }

    public string? PlayerName { get; init; }

    public string? TeamName { get; init; }

    public required PredictionMarketType Market { get; init; }

    public decimal? Line { get; init; }

    public decimal? PlaybookProjection { get; init; }

    /// <summary>Estimated probability (0–100) that the chosen direction hits.</summary>
    public required int Probability { get; init; }

    /// <summary>
    /// Edge vs the market in market units (yards, receptions, etc.),
    /// already confidence/volatility adjusted for ranking.
    /// </summary>
    public required decimal Edge { get; init; }

    /// <summary>Playbook confidence in this prediction (0–100).</summary>
    public required int Confidence { get; init; }

    public required PredictionDirection Direction { get; init; }

    public required string Reasoning { get; init; }

    public required IReadOnlyList<string> SupportingIntelligence { get; init; }

    /// <summary>Structured signal contributions for weight tuning / explainability.</summary>
    public IReadOnlyList<PredictionSignalContribution> SignalContributions { get; init; } = [];

    /// <summary>Expandable calculation detail — not shown by default in the board.</summary>
    public required IReadOnlyList<string> CalculationNotes { get; init; }

    public required string Source { get; init; }

    public required PropLineFreshness LineFreshness { get; init; }

    public required DateTimeOffset LastUpdated { get; init; }

    public string? Bookmaker { get; init; }

    /// <summary>Engine version that produced this prediction (e.g. 0.2).</summary>
    public string EngineVersion { get; init; } = "0.1";

    /// <summary>Composite ranking score (edge × confidence × probability lean).</summary>
    public decimal OpportunityScore { get; init; }

    public string SubjectLabel =>
        !string.IsNullOrWhiteSpace(PlayerName) ? PlayerName! :
        !string.IsNullOrWhiteSpace(TeamName) ? TeamName! :
        Event.DisplayName;

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

    public string DirectionLabel => Direction switch
    {
        PredictionDirection.Over => "Over",
        PredictionDirection.Under => "Under",
        PredictionDirection.Yes => "Yes",
        PredictionDirection.No => "No",
        PredictionDirection.Home => "Home",
        PredictionDirection.Away => "Away",
        PredictionDirection.Cover => "Cover",
        PredictionDirection.NotCover => "Not Cover",
        _ => Direction.ToString()
    };
}
