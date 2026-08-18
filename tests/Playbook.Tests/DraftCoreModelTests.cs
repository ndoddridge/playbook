using Playbook.Core.Draft;

namespace Playbook.Tests;

public class DraftCoreModelTests
{
    [Theory]
    [InlineData(1, 12, 1)]
    [InlineData(12, 12, 12)]
    [InlineData(13, 12, 12)]
    [InlineData(24, 12, 1)]
    [InlineData(25, 12, 1)]
    public void SlotForPick_Snake_Reverses_Every_Other_Round(int pickNumber, int teamCount, int expectedSlot)
    {
        Assert.Equal(expectedSlot, DraftOrderCalculator.SlotForPick(pickNumber, teamCount, isSnake: true));
    }

    [Theory]
    [InlineData(1, 12, 1)]
    [InlineData(12, 12, 12)]
    [InlineData(13, 12, 1)]
    [InlineData(24, 12, 12)]
    public void SlotForPick_Linear_Never_Reverses(int pickNumber, int teamCount, int expectedSlot)
    {
        Assert.Equal(expectedSlot, DraftOrderCalculator.SlotForPick(pickNumber, teamCount, isSnake: false));
    }

    [Fact]
    public void SlotForPick_Returns_Zero_For_Invalid_Input()
    {
        Assert.Equal(0, DraftOrderCalculator.SlotForPick(0, 12, isSnake: true));
        Assert.Equal(0, DraftOrderCalculator.SlotForPick(1, 0, isSnake: true));
    }

    [Theory]
    [InlineData(1, 12, 1)]
    [InlineData(12, 12, 1)]
    [InlineData(13, 12, 2)]
    [InlineData(24, 12, 2)]
    [InlineData(25, 12, 3)]
    public void RoundForPick_Computes_Correct_Round(int pickNumber, int teamCount, int expectedRound)
    {
        Assert.Equal(expectedRound, DraftOrderCalculator.RoundForPick(pickNumber, teamCount));
    }

    [Fact]
    public void NextPickForRoster_Finds_The_Snake_Wraparound_Pick_Correctly()
    {
        // 4-team snake: slots on the clock are 1,2,3,4 | 4,3,2,1 | 1,2,3,4 ...
        var slotToRosterId = new Dictionary<int, int> { [1] = 10, [2] = 20, [3] = 30, [4] = 40 };

        // Roster 10 (slot 1) picks 1st, then 8th (last of round 2, snake reverses), then 9th.
        Assert.Equal(1, DraftOrderCalculator.NextPickForRoster(1, 4, isSnake: true, totalPicks: 16, rosterId: 10, slotToRosterId));
        Assert.Equal(8, DraftOrderCalculator.NextPickForRoster(2, 4, isSnake: true, totalPicks: 16, rosterId: 10, slotToRosterId));
        Assert.Equal(9, DraftOrderCalculator.NextPickForRoster(9, 4, isSnake: true, totalPicks: 16, rosterId: 10, slotToRosterId));
    }

    [Fact]
    public void NextPickForRoster_Is_Immediate_When_The_Roster_Is_Already_On_The_Clock()
    {
        var slotToRosterId = new Dictionary<int, int> { [1] = 10, [2] = 20 };

        Assert.Equal(5, DraftOrderCalculator.NextPickForRoster(5, 2, isSnake: true, totalPicks: 20, rosterId: 10, slotToRosterId));
    }

    [Fact]
    public void NextPickForRoster_Returns_Null_When_The_Roster_Never_Comes_On_The_Clock()
    {
        var slotToRosterId = new Dictionary<int, int> { [1] = 10 };

        Assert.Null(DraftOrderCalculator.NextPickForRoster(1, 4, isSnake: true, totalPicks: 8, rosterId: 999, slotToRosterId));
    }

    [Fact]
    public void NextPickForRoster_Respects_The_Total_Picks_Bound_So_It_Cannot_Loop_Forever()
    {
        var slotToRosterId = new Dictionary<int, int> { [1] = 10, [2] = 20 };

        // Roster 10 owns slot 1 but the draft ends at pick 2 — looking past that must return null.
        Assert.Null(DraftOrderCalculator.NextPickForRoster(3, 2, isSnake: true, totalPicks: 2, rosterId: 10, slotToRosterId));
    }

    [Fact]
    public void DetectNewPicks_Returns_Only_Picks_Made_Since_Previous_Poll()
    {
        var previous = new List<DraftPickRecord>
        {
            MakePick(1, made: true),
            MakePick(2, made: false)
        };
        var current = new List<DraftPickRecord>
        {
            MakePick(1, made: true),
            MakePick(2, made: true),
            MakePick(3, made: true)
        };

        var newPicks = DraftPickDiff.DetectNewPicks(previous, current);

        Assert.Equal([2, 3], newPicks.Select(p => p.PickNumber));
    }

    [Fact]
    public void DetectNewPicks_Returns_Empty_When_Nothing_Changed_Since_Last_Poll()
    {
        var picks = new List<DraftPickRecord> { MakePick(1, made: true) };

        var newPicks = DraftPickDiff.DetectNewPicks(picks, picks);

        Assert.Empty(newPicks);
    }

    [Fact]
    public void DetectNewPicks_Ignores_Slots_That_Still_Have_No_Player()
    {
        var previous = new List<DraftPickRecord> { MakePick(1, made: false) };
        var current = new List<DraftPickRecord> { MakePick(1, made: false) };

        Assert.Empty(DraftPickDiff.DetectNewPicks(previous, current));
    }

    [Fact]
    public void IsUserOnTheClock_True_Only_When_Drafting_And_Next_Roster_Matches_User()
    {
        var board = MakeBoard(status: DraftStatus.Drafting, nextRosterId: 3, userRosterId: 3);
        Assert.True(board.IsUserOnTheClock);
    }

    [Fact]
    public void IsUserOnTheClock_False_When_Not_The_Users_Turn()
    {
        var board = MakeBoard(status: DraftStatus.Drafting, nextRosterId: 5, userRosterId: 3);
        Assert.False(board.IsUserOnTheClock);
    }

    [Fact]
    public void IsUserOnTheClock_False_When_Draft_Not_Actively_Drafting()
    {
        var board = MakeBoard(status: DraftStatus.Paused, nextRosterId: 3, userRosterId: 3);
        Assert.False(board.IsUserOnTheClock);
    }

    [Fact]
    public void IsComplete_True_When_Status_Reports_Complete()
    {
        var board = MakeBoard(status: DraftStatus.Complete, nextRosterId: null, userRosterId: null,
            totalRounds: 4, teamCount: 12, nextPickNumber: 10);
        Assert.True(board.IsComplete);
    }

    [Fact]
    public void IsComplete_True_When_Every_Pick_Slot_Is_Exhausted()
    {
        var board = MakeBoard(status: DraftStatus.Drafting, nextRosterId: null, userRosterId: null,
            totalRounds: 4, teamCount: 12, nextPickNumber: 49);
        Assert.True(board.IsComplete);
    }

    [Fact]
    public void IsComplete_False_While_Picks_Remain()
    {
        var board = MakeBoard(status: DraftStatus.Drafting, nextRosterId: 1, userRosterId: 1,
            totalRounds: 4, teamCount: 12, nextPickNumber: 48);
        Assert.False(board.IsComplete);
    }

    private static DraftPickRecord MakePick(int pickNumber, bool made) => new()
    {
        PickNumber = pickNumber,
        Round = 1,
        DraftSlot = pickNumber,
        PlayerId = made ? Guid.NewGuid() : null
    };

    private static DraftBoard MakeBoard(
        DraftStatus status, int? nextRosterId, int? userRosterId,
        int totalRounds = 4, int teamCount = 12, int nextPickNumber = 1) => new()
    {
        DraftId = "d1",
        LeagueId = Guid.NewGuid(),
        Season = "2026",
        Status = status,
        Type = "snake",
        TotalRounds = totalRounds,
        TeamCount = teamCount,
        Picks = [],
        NextPickNumber = nextPickNumber,
        NextRosterId = nextRosterId,
        UserRosterId = userRosterId,
        RetrievedAt = DateTimeOffset.UtcNow
    };
}
