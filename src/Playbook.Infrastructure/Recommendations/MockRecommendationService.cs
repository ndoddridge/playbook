using Playbook.Application.Recommendations;
using Playbook.Core.Recommendations;

namespace Playbook.Infrastructure.Recommendations;

/// <summary>
/// In-memory mock recommendations. Replace with engine-backed aggregation later.
/// </summary>
public sealed class MockRecommendationService : IRecommendationService
{
    private readonly IReadOnlyList<Recommendation> _recommendations;

    public MockRecommendationService()
    {
        var now = new DateTimeOffset(2026, 8, 7, 14, 30, 0, TimeSpan.FromHours(-7));

        _recommendations =
        [
            new Recommendation
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Title = "Start Jayden Daniels",
                Summary = "Elevated weekly outlook with a favorable matchup and clean supporting cast.",
                ActionType = RecommendationType.Start,
                Priority = RecommendationPriority.Critical,
                Confidence = 91,
                Impact = "+6.3% Win Probability",
                Category = RecommendationCategory.Lineup,
                Status = RecommendationStatus.Active,
                Reasoning = "Projected edge comes from pace, red-zone opportunity, and opponent pass efficiency allowed.",
                SupportingSignals =
                [
                    "Opponent allows top-8 QB fantasy points per game",
                    "Projected game script supports multi-score environment",
                    "No practice limitations reported"
                ],
                Evidence =
                [
                    "Mock projection: 21.4 expected points",
                    "Mock floor/ceiling: 14.1 / 29.8"
                ],
                FutureNotes = "Revisit if weather or inactive status changes before lock.",
                LastUpdated = now.AddMinutes(-18),
                SourceEngine = EngineType.Decision,
                RelatedPlayerId = Guid.Parse("11111111-1111-1111-1111-111111111101"),
                IsExpanded = false
            },
            new Recommendation
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Title = "Hold Bucky Irving",
                Summary = "Short-term noise should not override rising opportunity and role trajectory.",
                ActionType = RecommendationType.Hold,
                Priority = RecommendationPriority.High,
                Confidence = 84,
                Impact = "League Winning Potential",
                Category = RecommendationCategory.Roster,
                Status = RecommendationStatus.Watching,
                Reasoning = "Usage trend and efficiency markers remain constructive despite a quieter recent outing.",
                SupportingSignals =
                [
                    "Snap share trending upward across recent weeks",
                    "Route participation supports receiving floor",
                    "Depth-chart competition remains manageable"
                ],
                Evidence =
                [
                    "Mock rest-of-season rank holds inside starter range",
                    "Drop candidates elsewhere offer weaker upside"
                ],
                FutureNotes = "Convert to Start guidance if workload clears a sustained threshold.",
                LastUpdated = now.AddHours(-2),
                SourceEngine = EngineType.Projection,
                RelatedPlayerId = Guid.Parse("11111111-1111-1111-1111-111111111104"),
                IsExpanded = false
            },
            new Recommendation
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Title = "Add Rookie RB before waivers",
                Summary = "Priority stash with expanding opportunity if the current lead back is limited.",
                ActionType = RecommendationType.Add,
                Priority = RecommendationPriority.High,
                Confidence = 78,
                Impact = "High Value",
                Category = RecommendationCategory.Waivers,
                Status = RecommendationStatus.Active,
                Reasoning = "Availability and role pathway create asymmetric upside relative to roster cost.",
                SupportingSignals =
                [
                    "Practice reports hint at increased early-down work",
                    "Waiver competition expected to rise by midweek",
                    "Roster construction has an open flex-capable slot"
                ],
                Evidence =
                [
                    "Mock free-agent rank: top-5 available RB",
                    "Ownership still below breakout threshold"
                ],
                FutureNotes = "Place claim only if bench flexibility remains after other adds.",
                LastUpdated = now.AddHours(-5),
                SourceEngine = EngineType.Waiver,
                RelatedPlayerId = Guid.Parse("11111111-1111-1111-1111-111111111107"),
                IsExpanded = false
            },
            new Recommendation
            {
                Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                Title = "Trade veteran WR while value high",
                Summary = "Sell-high window remains open before schedule difficulty increases.",
                ActionType = RecommendationType.Trade,
                Priority = RecommendationPriority.Medium,
                Confidence = 73,
                Impact = "Low Risk",
                Category = RecommendationCategory.Trades,
                Status = RecommendationStatus.Active,
                Reasoning = "Market value currently outpaces rest-of-season projection confidence.",
                SupportingSignals =
                [
                    "Recent spike in target share may regress",
                    "Upcoming opponents rank well against perimeter receivers",
                    "Roster needs stronger RB contingency"
                ],
                Evidence =
                [
                    "Mock trade market: +1 starter-tier RB package interest",
                    "Projection delta favors rebalancing toward rushers"
                ],
                FutureNotes = "Do not force if counteroffers erode positional balance.",
                LastUpdated = now.AddHours(-9),
                SourceEngine = EngineType.Trade,
                RelatedPlayerId = Guid.Parse("11111111-1111-1111-1111-111111111110"),
                IsExpanded = false
            },
            new Recommendation
            {
                Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                Title = "Bench boom/bust flex option",
                Summary = "Safer floor alternative reduces weekly volatility without sacrificing much ceiling.",
                ActionType = RecommendationType.Bench,
                Priority = RecommendationPriority.Low,
                Confidence = 69,
                Impact = "Low Risk",
                Category = RecommendationCategory.Lineup,
                Status = RecommendationStatus.Watching,
                Reasoning = "Matchup and volatility profile favor the steadier flex candidate this week.",
                SupportingSignals =
                [
                    "Opponent suppresses explosive plays",
                    "Recent target concentration is unstable",
                    "Alternative flex has stronger floor projection"
                ],
                Evidence =
                [
                    "Mock flex delta: -1.2 expected points, +18% floor retention"
                ],
                FutureNotes = "Flip back to Start if late news improves route volume.",
                LastUpdated = now.AddHours(-11),
                SourceEngine = EngineType.Decision,
                RelatedPlayerId = Guid.Parse("11111111-1111-1111-1111-111111111108"),
                IsExpanded = false
            }
        ];
    }

    public IReadOnlyList<Recommendation> GetRecommendations() => _recommendations;

    public IReadOnlyList<Recommendation> GetTopRecommendations(int count = 5) =>
        _recommendations
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.Confidence)
            .Take(Math.Max(0, count))
            .ToList();
}
