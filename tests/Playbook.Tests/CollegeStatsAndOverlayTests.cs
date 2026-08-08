using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Stats.Models;
using Playbook.Infrastructure.Stats;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class CollegeStatsAndOverlayTests
{
    [Fact]
    public void Rookie_Player_Receives_College_Statistics()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var stats = provider.GetRequiredService<IPlayerStatsService>();
        var collegeStatus = provider.GetRequiredService<ICollegeStatsSyncStatus>();
        var danielsId = Guid.Parse("11111111-1111-1111-1111-111111111101");

        var college = PlayerCareerSeasonPresentation.ForCollegeTab(stats.GetStatsForPlayer(danielsId));

        Assert.NotEmpty(college);
        Assert.Equal("LSU", college[0].CollegeSchool);
        Assert.True((college[0].PassYards ?? 0) > 0);
        Assert.True(collegeStatus.CollegePlayersLoaded >= 1);
        Assert.True(collegeStatus.CollegeSeasonsLoaded >= 1);
    }

    [Fact]
    public void Player_With_One_Nfl_Season_Keeps_College_In_Career_Selector()
    {
        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
        var rows = new List<PlayerSeasonStats>
        {
            Nfl(id, 2025, StatsPeriod.CompletedSeason),
            College(id, 2024, "Ole Miss")
        };

        var career = PlayerCareerSeasonPresentation.ForCareerSelector(rows, yearsPro: 1);
        Assert.Contains(career, r => r.Period == StatsPeriod.College);
        Assert.Contains(career, r => r.Period == StatsPeriod.CompletedSeason);
    }

    [Fact]
    public void Player_With_Multiple_Nfl_Seasons_Still_Exposes_College_Tab()
    {
        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
        var rows = new List<PlayerSeasonStats>
        {
            Nfl(id, 2025, StatsPeriod.CurrentSeason),
            Nfl(id, 2024, StatsPeriod.CompletedSeason),
            College(id, 2023, "Georgia")
        };

        var career = PlayerCareerSeasonPresentation.ForCareerSelector(rows, yearsPro: 2);
        var college = PlayerCareerSeasonPresentation.ForCollegeTab(rows);

        Assert.Contains(career, r => r.Period == StatsPeriod.College);
        Assert.Single(college);
        Assert.Equal("Georgia", college[0].CollegeSchool);
    }

    [Fact]
    public void Veteran_With_Three_Plus_Nfl_Seasons_Does_Not_Promote_College_In_Career()
    {
        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3");
        var rows = new List<PlayerSeasonStats>
        {
            Nfl(id, 2025, StatsPeriod.CurrentSeason),
            Nfl(id, 2024, StatsPeriod.CompletedSeason),
            Nfl(id, 2023, StatsPeriod.CompletedSeason),
            College(id, 2016, "Texas Tech")
        };

        var career = PlayerCareerSeasonPresentation.ForCareerSelector(rows, yearsPro: 9);
        var college = PlayerCareerSeasonPresentation.ForCollegeTab(rows);

        Assert.DoesNotContain(career, r => r.Period == StatsPeriod.College);
        Assert.NotEmpty(college);
    }

    [Fact]
    public void Missing_College_Data_Is_Not_Fabricated()
    {
        using var provider = TestServiceFactory.CreateProvider(statsProvider: PlayerStatsProviderKind.Mock);
        var stats = provider.GetRequiredService<IPlayerStatsService>();
        var mahomesId = Guid.Parse("11111111-1111-1111-1111-111111111103");

        var college = PlayerCareerSeasonPresentation.ForCollegeTab(stats.GetStatsForPlayer(mahomesId));
        Assert.Empty(college);
    }

    [Fact]
    public void Modal_Layout_Rules_Support_Narrow_Viewport()
    {
        Assert.Equal(390, PlayerOverlayLayoutRules.NarrowViewportWidthPx);
        Assert.Equal(7, PlayerOverlayLayoutRules.Tabs.Length);
        Assert.Contains("College", PlayerOverlayLayoutRules.Tabs);
        Assert.Contains("Career", PlayerOverlayLayoutRules.Tabs);

        Assert.True(PlayerOverlayLayoutRules.TabStripFitsWithoutHorizontalPageOverflow(
            PlayerOverlayLayoutRules.NarrowViewportWidthPx,
            tabCount: PlayerOverlayLayoutRules.Tabs.Length,
            approximateTabWidthPx: 88));
    }

    [Fact]
    public void Long_Season_Names_Remain_Selectable()
    {
        var label = PlayerCareerSeasonPresentation.FormatSeasonOption(College(
            Guid.NewGuid(),
            2024,
            "Mississippi State Bulldogs"));

        Assert.Contains("College Season", label);
        Assert.Contains("Mississippi State Bulldogs", label);
        Assert.True(PlayerOverlayLayoutRules.SeasonOptionIsFullyVisibleInSelect(label, selectWidthPx: 320));
    }

    [Fact]
    public void Tab_Navigation_Contract_Avoids_Horizontal_Page_Overflow()
    {
        Assert.Equal("scroll-x", PlayerOverlayLayoutRules.TabsOverflowStrategy);

        var cssPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Playbook.Web", "wwwroot", "css", "player-overlay.css"));
        Assert.True(File.Exists(cssPath), $"Expected overlay CSS at {cssPath}");

        var css = File.ReadAllText(cssPath);
        Assert.Contains("overflow-x: auto", css);
        Assert.Contains("overflow-x: hidden", css);
        Assert.Contains("100dvh", css);
        Assert.Contains("min-width: 0", css);
        Assert.Contains("player-overlay__season-select", css);
    }

    [Fact]
    public async Task Mock_College_Provider_Returns_Only_College_Period()
    {
        var mock = new MockCollegeStatsProvider();
        var rows = await mock.GetCollegeStatsAsync(new CollegeStatsSyncRequest
        {
            Candidates =
            [
                new CollegePlayerCandidate
                {
                    PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111101"),
                    FullName = "Jayden Daniels",
                    FirstName = "Jayden",
                    LastName = "Daniels",
                    YearsPro = 2,
                    College = "LSU"
                }
            ]
        });

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(StatsPeriod.College, r.Period));
        Assert.All(rows, r => Assert.True(r.HasAnyCountingStat));
    }

    [Fact]
    public void Period_Labels_Distinguish_Current_Completed_And_College()
    {
        Assert.Equal("Current Season (NFL)", PlayerCareerSeasonPresentation.FormatPeriodLabel(StatsPeriod.CurrentSeason));
        Assert.Equal("Completed Season (NFL)", PlayerCareerSeasonPresentation.FormatPeriodLabel(StatsPeriod.CompletedSeason));
        Assert.Equal("College Season", PlayerCareerSeasonPresentation.FormatPeriodLabel(StatsPeriod.College));
    }

    private static PlayerSeasonStats Nfl(Guid id, int season, StatsPeriod period) =>
        new()
        {
            PlayerId = id,
            Season = season,
            SeasonType = "regular",
            Period = period,
            Games = 10,
            PassYards = 2000,
            FantasyPointsPpr = 180,
            SourceProvider = "Test",
            LastUpdated = DateTimeOffset.UtcNow
        };

    private static PlayerSeasonStats College(Guid id, int season, string school) =>
        new()
        {
            PlayerId = id,
            Season = season,
            SeasonType = "college",
            Period = StatsPeriod.College,
            Games = 12,
            PassYards = 3000,
            CollegeSchool = school,
            FantasyPointsPpr = 220,
            SourceProvider = "Test",
            LastUpdated = DateTimeOffset.UtcNow
        };
}
