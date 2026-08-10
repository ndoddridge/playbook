using Playbook.Core.Predictions;

namespace Playbook.Web.Features.QuickPicks.Models;

/// <summary>Client-side Quick Picks filters — always scoped to the selected slate.</summary>
public sealed class QuickPickFilters
{
    public string Search { get; set; } = "";

    public PredictionMarketType? Market { get; set; }

    public PredictionDirection? Direction { get; set; }

    public int MinConfidence { get; set; }

    /// <summary>When true, emphasize Top/strongest; when false, include broader slate props.</summary>
    public bool StrongestOnly { get; set; } = true;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Search) ||
        Market is not null ||
        Direction is not null ||
        MinConfidence > 0;

    public void Clear()
    {
        Search = "";
        Market = null;
        Direction = null;
        MinConfidence = 0;
        StrongestOnly = true;
    }

    public IEnumerable<Prediction> Apply(IEnumerable<Prediction> source)
    {
        var q = source;
        var term = Search.Trim();
        if (term.Length > 0)
        {
            q = q.Where(p => MatchesSearch(p, term));
        }

        if (Market is PredictionMarketType market)
        {
            q = q.Where(p => p.Market == market);
        }

        if (Direction is PredictionDirection direction)
        {
            q = q.Where(p => p.Direction == direction);
        }

        if (MinConfidence > 0)
        {
            q = q.Where(p => p.Confidence >= MinConfidence);
        }

        return q;
    }

    private static bool MatchesSearch(Prediction p, string term)
    {
        if (p.PlayerName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (p.TeamName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (p.Event.HomeTeam.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.Event.AwayTeam.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.Event.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.Event.MatchupKey.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (p.MarketLabel.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Lightweight aliases
        var aliases = term.ToLowerInvariant() switch
        {
            "buffalo" or "bills" => "BUF",
            "receiving" or "rec" => "Receiving",
            "passing" or "pass" => "Passing",
            "rushing" or "rush" => "Rushing",
            _ => null
        };
        if (aliases is not null)
        {
            return MatchesSearch(p, aliases);
        }

        return false;
    }
}
