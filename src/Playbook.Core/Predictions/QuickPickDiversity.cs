namespace Playbook.Core.Predictions;

/// <summary>
/// Selects Top Picks as the strongest <em>distinct</em> opportunities.
/// Applies similarity penalties — does not change underlying confidence/edge/probability.
/// </summary>
public static class QuickPickDiversity
{
    /// <summary>Near-duplicate (alternate lines of the same bet) — excluded from Top rather than padded in.</summary>
    public const double NearDuplicateThreshold = 0.9;

    /// <summary>How hard to penalize moderate similarity when choosing among remaining candidates.</summary>
    public const double DiversityPenaltyWeight = 0.8;

    /// <summary>
    /// Greedy diverse selection from an already strength-ranked candidate list.
    /// Returns at most <paramref name="count"/> picks; fewer when only near-duplicates remain.
    /// </summary>
    public static IReadOnlyList<Prediction> SelectTop(
        IReadOnlyList<Prediction> rankedCandidates,
        int count)
    {
        count = Math.Clamp(count, 1, 20);
        if (rankedCandidates.Count == 0)
        {
            return [];
        }

        var selected = new List<Prediction>(count);
        var remaining = rankedCandidates.ToList();

        while (selected.Count < count && remaining.Count > 0)
        {
            Prediction? best = null;
            var bestAdjusted = decimal.MinValue;
            var bestIndex = -1;

            for (var i = 0; i < remaining.Count; i++)
            {
                var candidate = remaining[i];
                var maxSimilarity = selected.Count == 0
                    ? 0d
                    : selected.Max(s => Similarity(candidate, s));

                // Prefer leaving near-duplicates for Watch instead of padding Top Picks.
                if (maxSimilarity >= NearDuplicateThreshold)
                {
                    continue;
                }

                var strength = Math.Max(0.01m, candidate.OpportunityScore);
                var factor = (decimal)(1d - (DiversityPenaltyWeight * maxSimilarity));
                var adjusted = strength * Math.Max(0.05m, factor);

                // Tiny tie-break: prefer original strength order.
                adjusted += candidate.OpportunityScore * 0.0001m;

                if (adjusted > bestAdjusted)
                {
                    bestAdjusted = adjusted;
                    best = candidate;
                    bestIndex = i;
                }
            }

            if (best is null || bestIndex < 0)
            {
                // Only near-duplicates left — stop rather than filling with clones.
                break;
            }

            selected.Add(best);
            remaining.RemoveAt(bestIndex);
        }

        return selected;
    }

    /// <summary>
    /// Similarity in [0, 1]. 1 = same underlying opportunity (e.g. alternate lines).
    /// </summary>
    public static double Similarity(Prediction a, Prediction b)
    {
        if (ReferenceEquals(a, b) || a.Id == b.Id)
        {
            return 1d;
        }

        if (string.Equals(OpportunityKey(a), OpportunityKey(b), StringComparison.Ordinal))
        {
            return 1d;
        }

        var samePlayer = SamePlayer(a, b);
        var sameTeam = SameTeam(a, b);
        var sameEvent = string.Equals(a.Event.EventId, b.Event.EventId, StringComparison.OrdinalIgnoreCase);
        var sameMarket = a.Market == b.Market;
        var sameDirFamily = DirectionFamily(a.Direction) == DirectionFamily(b.Direction);

        // Same player + same market (alternate lines / books)
        if (samePlayer && sameMarket)
        {
            return 0.98d;
        }

        // Same team subject + same market + same side family (ATL Not Cover 5.5 vs 4.5)
        if (sameTeam && sameMarket && sameDirFamily && IsTeamOrGameMarket(a.Market))
        {
            return 0.96d;
        }

        // Same matchup + same game market + same side (e.g. game-total alt lines)
        if (sameEvent && sameMarket && sameDirFamily &&
            a.Market is PredictionMarketType.GameTotal)
        {
            return 0.94d;
        }

        // Correlated game markets in the same matchup (spread ↔ moneyline ↔ team total)
        if (sameEvent && CorrelatedGameMarkets(a, b))
        {
            return 0.82d;
        }

        // Same player, different market
        if (samePlayer)
        {
            return 0.55d;
        }

        // Soft preferences: already used this team or market type
        var soft = 0d;
        if (sameTeam)
        {
            soft = Math.Max(soft, 0.42d);
        }

        if (sameMarket)
        {
            soft = Math.Max(soft, 0.28d);
        }

        if (sameEvent)
        {
            soft = Math.Max(soft, 0.32d);
        }

        if (MarketCategory(a.Market) == MarketCategory(b.Market) &&
            MarketCategory(a.Market) != "other")
        {
            soft = Math.Max(soft, 0.22d);
        }

        return soft;
    }

    /// <summary>
    /// Stable key for the underlying opportunity, ignoring line / book / exact number.
    /// </summary>
    public static string OpportunityKey(Prediction p)
    {
        var dir = DirectionFamily(p.Direction);
        if (!string.IsNullOrWhiteSpace(p.PlayerName) || p.PlayerId is not null)
        {
            var player = p.PlayerId?.ToString("N")
                         ?? p.PlayerName!.Trim().ToUpperInvariant();
            return $"P|{player}|{p.Market}|{dir}";
        }

        if (p.Market == PredictionMarketType.GameTotal)
        {
            return $"G|{p.Event.EventId}|GameTotal|{dir}";
        }

        var team = (p.TeamName ?? SubjectTeamFallback(p)).Trim().ToUpperInvariant();
        return $"T|{p.Event.EventId}|{team}|{p.Market}|{dir}";
    }

    private static string SubjectTeamFallback(Prediction p)
    {
        // Spread/ML direction sometimes encodes side without TeamName.
        return p.Direction switch
        {
            PredictionDirection.Home => p.Event.HomeTeam,
            PredictionDirection.Away => p.Event.AwayTeam,
            _ => p.TeamName ?? p.SubjectLabel
        };
    }

    private static bool SamePlayer(Prediction a, Prediction b)
    {
        if (a.PlayerId is Guid idA && b.PlayerId is Guid idB)
        {
            return idA == idB;
        }

        return !string.IsNullOrWhiteSpace(a.PlayerName) &&
               !string.IsNullOrWhiteSpace(b.PlayerName) &&
               string.Equals(a.PlayerName, b.PlayerName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameTeam(Prediction a, Prediction b)
    {
        var teamA = (a.TeamName ?? SubjectTeamFallback(a)).Trim();
        var teamB = (b.TeamName ?? SubjectTeamFallback(b)).Trim();
        if (teamA.Length == 0 || teamB.Length == 0)
        {
            return false;
        }

        return string.Equals(teamA, teamB, StringComparison.OrdinalIgnoreCase) ||
               NflTeamCatalog.TeamMatchesQuery(teamA, teamB);
    }

    private static bool IsTeamOrGameMarket(PredictionMarketType market) =>
        market is PredictionMarketType.Spread
            or PredictionMarketType.Winner
            or PredictionMarketType.TeamTotal
            or PredictionMarketType.GameTotal;

    private static bool CorrelatedGameMarkets(Prediction a, Prediction b)
    {
        if (!IsTeamOrGameMarket(a.Market) || !IsTeamOrGameMarket(b.Market))
        {
            return false;
        }

        // Game total vs team total / spread is related but more distinct than alt spreads.
        if (a.Market == PredictionMarketType.GameTotal || b.Market == PredictionMarketType.GameTotal)
        {
            return a.Market != PredictionMarketType.GameTotal &&
                   b.Market != PredictionMarketType.GameTotal
                ? false
                : SameTeam(a, b) || a.Market == b.Market;
        }

        // Spread ↔ moneyline ↔ team total for the same side/team
        return SameTeam(a, b) && DirectionFamily(a.Direction) == DirectionFamily(b.Direction);
    }

    private static string DirectionFamily(PredictionDirection direction) => direction switch
    {
        PredictionDirection.Over or PredictionDirection.Yes or PredictionDirection.Cover
            or PredictionDirection.Home => "pos",
        PredictionDirection.Under or PredictionDirection.No or PredictionDirection.NotCover
            or PredictionDirection.Away => "neg",
        _ => direction.ToString()
    };

    private static string MarketCategory(PredictionMarketType market) => market switch
    {
        PredictionMarketType.PassingYards or PredictionMarketType.PassingTouchdowns => "pass",
        PredictionMarketType.RushingYards => "rush",
        PredictionMarketType.ReceivingYards or PredictionMarketType.Receptions => "rec",
        PredictionMarketType.AnytimeTouchdown => "td",
        PredictionMarketType.Spread or PredictionMarketType.Winner => "side",
        PredictionMarketType.TeamTotal or PredictionMarketType.GameTotal => "total",
        _ => "other"
    };
}
