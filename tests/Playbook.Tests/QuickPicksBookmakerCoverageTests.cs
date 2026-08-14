using Playbook.Core.Predictions;
using Playbook.Infrastructure.Predictions;

namespace Playbook.Tests;

/// <summary>
/// Covers the honest-transparency diagnostic added after confirming (against the real Odds API)
/// that Caesars/williamhill_us currently returns no lines for any sport — the priority/selection
/// logic itself (<see cref="LivePropLineProviderBookmakerPriorityTests"/>) was already correct.
/// </summary>
public class QuickPicksBookmakerCoverageTests
{
    private static readonly IReadOnlyDictionary<string, string> FriendlyNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["williamhill_us"] = "Caesars Sportsbook",
            ["draftkings"] = "DraftKings"
        };

    [Fact]
    public void Reports_Primary_Inactive_When_Configured_Book_Supplied_No_Lines()
    {
        var lines = new[] { MakeLine("DraftKings"), MakeLine("DraftKings"), MakeLine("FanDuel") };
        var priority = LivePropLineProvider.ParseBookmakerPriority("williamhill_us,draftkings,fanduel");

        var (primaryName, primaryActive, summary) =
            QuickPicksService.ComputeBookmakerCoverage(lines, priority, FriendlyNames);

        Assert.Equal("Caesars Sportsbook", primaryName);
        Assert.False(primaryActive);
        Assert.Contains("DraftKings (2)", summary);
    }

    [Fact]
    public void Reports_Primary_Active_When_Configured_Book_Did_Supply_A_Line()
    {
        var lines = new[] { MakeLine("Caesars Sportsbook"), MakeLine("DraftKings") };
        var priority = LivePropLineProvider.ParseBookmakerPriority("williamhill_us,draftkings");

        var (_, primaryActive, _) = QuickPicksService.ComputeBookmakerCoverage(lines, priority, FriendlyNames);

        Assert.True(primaryActive);
    }

    [Fact]
    public void Mock_Sourced_Lines_Are_Never_Counted_As_Bookmaker_Coverage()
    {
        var lines = new[] { MakeLine("MockBook", source: "Mock") };
        var priority = LivePropLineProvider.ParseBookmakerPriority("williamhill_us,draftkings");

        var (_, primaryActive, summary) = QuickPicksService.ComputeBookmakerCoverage(lines, priority, FriendlyNames);

        Assert.False(primaryActive);
        Assert.Equal("none", summary);
    }

    [Fact]
    public void No_Configured_Priority_Returns_Null_Primary_Without_Throwing()
    {
        var (primaryName, primaryActive, _) =
            QuickPicksService.ComputeBookmakerCoverage([], [], FriendlyNames);

        Assert.Null(primaryName);
        Assert.False(primaryActive);
    }

    private static PropLine MakeLine(string bookmaker, string source = "TheOddsAPI") => new()
    {
        Id = $"test:{bookmaker}:{Guid.NewGuid()}",
        Event = new FootballEvent
        {
            EventId = "test-evt",
            HomeTeam = "CLE",
            AwayTeam = "CIN",
            CommenceTime = DateTimeOffset.UtcNow.AddDays(1)
        },
        Market = PredictionMarketType.Spread,
        Line = -3.5m,
        Bookmaker = bookmaker,
        Source = source,
        UpdatedAt = DateTimeOffset.UtcNow,
        Freshness = PropLineFreshness.Live
    };
}
