using Playbook.Core.Predictions;
using Playbook.Infrastructure.Predictions;

namespace Playbook.Tests;

/// <summary>
/// Caesars Sportsbook (The Odds API key <c>williamhill_us</c>) is the configured primary
/// bookmaker; other listed books only supplement markets Caesars doesn't have. These test the
/// pure ranking/selection logic directly — no HTTP involved.
/// </summary>
public class LivePropLineProviderBookmakerPriorityTests
{
    private static readonly IReadOnlyList<string> DefaultPriority =
        LivePropLineProvider.ParseBookmakerPriority("williamhill_us,draftkings,fanduel,betmgm");

    [Fact]
    public void ParseBookmakerPriority_Preserves_Csv_Order()
    {
        var priority = LivePropLineProvider.ParseBookmakerPriority("williamhill_us,draftkings,fanduel,betmgm");

        Assert.Equal(["williamhill_us", "draftkings", "fanduel", "betmgm"], priority);
    }

    [Fact]
    public void ParseBookmakerPriority_Lowercases_And_Deduplicates()
    {
        var priority = LivePropLineProvider.ParseBookmakerPriority("WilliamHill_US, DraftKings, williamhill_us");

        Assert.Equal(["williamhill_us", "draftkings"], priority);
    }

    [Fact]
    public void Caesars_Ranks_First_When_Configured_As_Primary()
    {
        var rank = LivePropLineProvider.BookmakerPriorityRank("williamhill_us", DefaultPriority);

        Assert.Equal(0, rank);
        Assert.True(rank < LivePropLineProvider.BookmakerPriorityRank("draftkings", DefaultPriority));
        Assert.True(rank < LivePropLineProvider.BookmakerPriorityRank("fanduel", DefaultPriority));
        Assert.True(rank < LivePropLineProvider.BookmakerPriorityRank("betmgm", DefaultPriority));
    }

    [Fact]
    public void Unlisted_Bookmaker_Ranks_After_Every_Listed_Book()
    {
        var unlistedRank = LivePropLineProvider.BookmakerPriorityRank("some_other_book", DefaultPriority);

        Assert.Equal(DefaultPriority.Count, unlistedRank);
        Assert.True(unlistedRank > LivePropLineProvider.BookmakerPriorityRank("betmgm", DefaultPriority));
    }

    [Fact]
    public void SelectPreferredPerIdentity_Keeps_Caesars_Line_When_Present()
    {
        var caesarsLine = MakeLine("Caesars", line: 94.5m);
        var draftKingsLine = MakeLine("DraftKings", line: 95.5m);

        // Priority order: Caesars first, so it must appear before DraftKings in the candidate list.
        var selected = LivePropLineProvider.SelectPreferredPerIdentity([caesarsLine, draftKingsLine]);

        var only = Assert.Single(selected);
        Assert.Equal("Caesars", only.Bookmaker);
        Assert.Equal(94.5m, only.Line);
    }

    [Fact]
    public void SelectPreferredPerIdentity_Supplements_With_Next_Book_When_Primary_Lacks_The_Market()
    {
        // Only DraftKings has this player's market — Caesars never posted it.
        var draftKingsLine = MakeLine("DraftKings", line: 95.5m);

        var selected = LivePropLineProvider.SelectPreferredPerIdentity([draftKingsLine]);

        var only = Assert.Single(selected);
        Assert.Equal("DraftKings", only.Bookmaker);
    }

    [Fact]
    public void SelectPreferredPerIdentity_Keeps_Lines_For_Different_Players_Separately()
    {
        var chase = MakeLine("Caesars", line: 94.5m, playerName: "Ja'Marr Chase");
        var jefferson = MakeLine("Caesars", line: 101.5m, playerName: "Justin Jefferson");

        var selected = LivePropLineProvider.SelectPreferredPerIdentity([chase, jefferson]);

        Assert.Equal(2, selected.Count);
    }

    private static PropLine MakeLine(string bookmaker, decimal line, string playerName = "Ja'Marr Chase") => new()
    {
        Id = $"test:{bookmaker}",
        Event = new FootballEvent
        {
            EventId = "test-cin-cle",
            HomeTeam = "CLE",
            AwayTeam = "CIN",
            CommenceTime = DateTimeOffset.UtcNow.AddDays(1)
        },
        PlayerName = playerName,
        TeamName = "CIN",
        Market = PredictionMarketType.ReceivingYards,
        Line = line,
        Bookmaker = bookmaker,
        Source = "TheOddsAPI",
        UpdatedAt = DateTimeOffset.UtcNow,
        Freshness = PropLineFreshness.Live
    };
}
