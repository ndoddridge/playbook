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
            q = q.Where(p => QuickPickSearch.Matches(p, term));
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

    /// <summary>
    /// Rank eligible (live/mock) props for Top Picks / Watch using existing opportunity scores.
    /// Does not alter confidence or probability — ranking only.
    /// </summary>
    public IReadOnlyList<Prediction> RankEligible(IEnumerable<Prediction> source)
    {
        var term = Search.Trim();
        return QuickPickSearch.RankEligible(
            source,
            p =>
            {
                if (term.Length > 0 && !QuickPickSearch.Matches(p, term))
                {
                    return false;
                }

                if (Market is PredictionMarketType market && p.Market != market)
                {
                    return false;
                }

                if (Direction is PredictionDirection direction && p.Direction != direction)
                {
                    return false;
                }

                if (MinConfidence > 0 && p.Confidence < MinConfidence)
                {
                    return false;
                }

                return true;
            });
    }
}
