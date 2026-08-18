using Playbook.Core.Draft;
using Playbook.Core.Historical;

namespace Playbook.Tests;

/// <summary>
/// Personal mock-learning is a pure function of a live/ingested board plus already-observed
/// decision history — no store dependency, so a single mock structurally cannot leak into any
/// persisted model. "One observation = weak evidence" is asserted directly against the same
/// evidence-strength buckets historical intelligence uses.
/// </summary>
public class PersonalDraftTendencyPolicyTests
{
    [Fact]
    public void Returns_Null_When_The_User_Has_Made_No_Picks_Yet()
    {
        var board = Board([Pick(1, 1, 1, rosterId: 2, position: "RB")]);

        var result = PersonalDraftTendencyPolicy.Compute(board, myRosterId: 1, decisionHistory: []);

        Assert.Null(result);
    }

    [Fact]
    public void A_Single_Pick_Is_Insufficient_Evidence()
    {
        var board = Board([Pick(1, 1, 1, rosterId: 1, position: "RB")]);

        var result = PersonalDraftTendencyPolicy.Compute(board, myRosterId: 1, decisionHistory: []);

        Assert.NotNull(result);
        Assert.Equal(1, result!.PickCount);
        Assert.Equal(HistoricalEvidenceStrength.Insufficient, result.EvidenceStrength);
    }

    [Fact]
    public void Repeated_Picks_Strengthen_Evidence_Same_Buckets_As_Historical_Intelligence()
    {
        var picks = Enumerable.Range(1, 6)
            .Select(i => Pick(i, i, 1, rosterId: 1, position: i % 2 == 0 ? "RB" : "WR"))
            .ToList();
        var board = Board(picks);

        var result = PersonalDraftTendencyPolicy.Compute(board, myRosterId: 1, decisionHistory: []);

        Assert.Equal(6, result!.PickCount);
        Assert.Equal(HistoricalEvidenceStrength.Moderate, result.EvidenceStrength);
    }

    [Fact]
    public void Position_Emphasis_Counts_Only_The_Users_Own_Made_Picks()
    {
        var board = Board([
            Pick(1, 1, 1, rosterId: 1, position: "RB"),
            Pick(2, 1, 2, rosterId: 2, position: "RB"), // someone else's pick — must not count
            Pick(3, 2, 1, rosterId: 1, position: "RB")
        ]);

        var result = PersonalDraftTendencyPolicy.Compute(board, myRosterId: 1, decisionHistory: []);

        Assert.Equal(2, result!.PickCount);
        var rb = Assert.Single(result.PositionEmphasis);
        Assert.Equal("RB", rb.Position);
        Assert.Equal(2, rb.PickCount);
        Assert.Contains("RB-heavy", result.RosterBuildPattern, StringComparison.Ordinal);
    }

    [Fact]
    public void Decision_History_Rolls_Up_Into_Category_Pick_Counts()
    {
        var board = Board([Pick(1, 1, 1, rosterId: 1, position: "WR")]);
        var history = new List<PersonalDraftDecision>
        {
            new(1, Guid.NewGuid(), "Upside WR", RecommendationCategory.BestUpside, PersonalDecisionAlignment.MatchedRecommendation)
        };

        var result = PersonalDraftTendencyPolicy.Compute(board, myRosterId: 1, decisionHistory: history);

        Assert.Equal(1, result!.CategoryPickCounts[RecommendationCategory.BestUpside]);
        Assert.Same(history, result.DecisionsVsRecommendations);
    }

    private static DraftBoard Board(IReadOnlyList<DraftPickRecord> picks) => new()
    {
        DraftId = "d1",
        LeagueId = Guid.NewGuid(),
        Season = "2024",
        Status = DraftStatus.Drafting,
        Type = "snake",
        TotalRounds = 15,
        TeamCount = 10,
        Picks = picks,
        NextPickNumber = picks.Count + 1,
        RetrievedAt = DateTimeOffset.UtcNow
    };

    private static DraftPickRecord Pick(int pickNumber, int round, int slot, int rosterId, string position) => new()
    {
        PickNumber = pickNumber,
        Round = round,
        DraftSlot = slot,
        RosterId = rosterId,
        PlayerId = Guid.NewGuid(),
        PlayerName = $"Player {pickNumber}",
        PositionLabel = position
    };
}
