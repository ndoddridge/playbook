namespace Playbook.Core.Predictions;

/// <summary>
/// Shared Quick Picks search + ranking helpers (team aliases, market shortcuts).
/// UI filters and services reuse this so alias behavior stays consistent.
/// </summary>
public static class QuickPickSearch
{
    public static bool Matches(Prediction p, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        if (p.PlayerName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (p.MarketLabel.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (MatchesMarketAlias(p, term))
        {
            return true;
        }

        var teamAbbrs = NflTeamCatalog.ResolveAbbreviations(term);
        if (teamAbbrs.Count > 0 && teamAbbrs.Any(abbr => TouchesTeam(p, abbr)))
        {
            return true;
        }

        if (NflTeamCatalog.TeamMatchesQuery(p.TeamName, term) ||
            NflTeamCatalog.TeamMatchesQuery(p.Event.HomeTeam, term) ||
            NflTeamCatalog.TeamMatchesQuery(p.Event.AwayTeam, term))
        {
            return true;
        }

        return p.TeamName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ||
               p.Event.HomeTeam.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               p.Event.AwayTeam.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               p.Event.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               p.Event.MatchupKey.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<Prediction> RankEligible(
        IEnumerable<Prediction> source,
        Func<Prediction, bool>? predicate = null)
    {
        var q = source.Where(p => p.LineFreshness is PropLineFreshness.Live or PropLineFreshness.Mock);
        if (predicate is not null)
        {
            q = q.Where(predicate);
        }

        return q
            .OrderByDescending(p => p.OpportunityScore)
            .ThenByDescending(p => p.Edge)
            .ThenByDescending(p => p.Probability)
            .ThenByDescending(p => p.Confidence)
            .ToList();
    }

    /// <summary>
    /// Strength-rank eligible props, then select the best diverse Top Picks subset.
    /// </summary>
    public static IReadOnlyList<Prediction> SelectDiverseTop(
        IEnumerable<Prediction> source,
        int count,
        Func<Prediction, bool>? predicate = null) =>
        QuickPickDiversity.SelectTop(RankEligible(source, predicate), count);

    private static bool TouchesTeam(Prediction p, string abbreviation) =>
        string.Equals(p.TeamName, abbreviation, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(p.Event.HomeTeam, abbreviation, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(p.Event.AwayTeam, abbreviation, StringComparison.OrdinalIgnoreCase) ||
        NflTeamCatalog.TeamMatchesQuery(p.TeamName, abbreviation) ||
        NflTeamCatalog.TeamMatchesQuery(p.Event.HomeTeam, abbreviation) ||
        NflTeamCatalog.TeamMatchesQuery(p.Event.AwayTeam, abbreviation);

    private static bool MatchesMarketAlias(Prediction p, string term)
    {
        var key = term.Trim().ToLowerInvariant();
        return key switch
        {
            "receiving" or "rec" or "receiving props" =>
                p.Market is PredictionMarketType.ReceivingYards or PredictionMarketType.Receptions,
            "passing" or "pass" or "passing props" =>
                p.Market is PredictionMarketType.PassingYards or PredictionMarketType.PassingTouchdowns,
            "rushing" or "rush" or "rushing props" =>
                p.Market == PredictionMarketType.RushingYards,
            "td" or "touchdown" or "touchdowns" =>
                p.Market is PredictionMarketType.AnytimeTouchdown or PredictionMarketType.PassingTouchdowns,
            _ => false
        };
    }
}
