using Playbook.Application.News;
using Playbook.Core.News;

namespace Playbook.Infrastructure.News;

public sealed class MockNewsProvider : INewsSource
{
    public NewsProviderKind Kind => NewsProviderKind.Mock;

    public string DisplayName => "Mock";

    public Task<IReadOnlyList<NewsArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<NewsArticle>>(CreateArticles());
    }

    private static IReadOnlyList<NewsArticle> CreateArticles()
    {
        var now = DateTimeOffset.Now;
        return
        [
            A("a1000001-0001-0001-0001-000000000001", "Jayden Daniels limited in practice with shoulder maintenance",
                "Washington lists Daniels as limited; expected to play Sunday if no setbacks.",
                now.AddHours(-2), NewsCategory.Injury, NewsPriority.High,
                ["Jayden Daniels"], ["WAS"]),
            A("a1000001-0001-0001-0001-000000000002", "Jordan Love dealing with finger wrap but full participant",
                "Love completed team drills after early limited work in Green Bay.",
                now.AddHours(-4), NewsCategory.Injury, NewsPriority.Normal,
                ["Jordan Love"], ["GB"]),
            A("a1000001-0001-0001-0001-000000000003", "Saquon Barkley expected to handle lead back role again",
                "Eagles coaches reaffirm Barkley as the clear RB1 entering Week 1.",
                now.AddHours(-6), NewsCategory.Performance, NewsPriority.Normal,
                ["Saquon Barkley"], ["PHI"]),
            A("a1000001-0001-0001-0001-000000000004", "CeeDee Lamb returns to full practice after ankle scare",
                "Lamb shed the limited tag and looks on track for a normal workload.",
                now.AddHours(-8), NewsCategory.Injury, NewsPriority.High,
                ["CeeDee Lamb"], ["DAL"]),
            A("a1000001-0001-0001-0001-000000000005", "Travis Kelce resting veterans days as Chiefs manage snaps",
                "Kansas City continues veteran rest patterns; no injury designation.",
                now.AddHours(-10), NewsCategory.TrainingCamp, NewsPriority.Low,
                ["Travis Kelce"], ["KC"]),
            A("a1000001-0001-0001-0001-000000000006", "Bucky Irving earning early-down work in Buccaneers camp",
                "Coaches praise Irving's vision; committee still expected early in season.",
                now.AddHours(-12), NewsCategory.Performance, NewsPriority.Normal,
                ["Bucky Irving"], ["TB"]),
            A("a1000001-0001-0001-0001-000000000007", "Patrick Mahomes: Chiefs offense installing new RPO package",
                "Mahomes highlighted timing with new skill players during walkthroughs.",
                now.AddHours(-14), NewsCategory.Analysis, NewsPriority.Normal,
                ["Patrick Mahomes"], ["KC"]),
            A("a1000001-0001-0001-0001-000000000008", "Breaking: League issues conduct memo ahead of Week 1",
                "NFL reiterates player conduct policies; no specific roster moves named.",
                now.AddHours(-1), NewsCategory.Breaking, NewsPriority.Critical,
                [], [])
        ];
    }

    private static NewsArticle A(
        string id,
        string title,
        string summary,
        DateTimeOffset published,
        NewsCategory category,
        NewsPriority priority,
        string[] players,
        string[] teams) =>
        new()
        {
            Id = Guid.Parse(id),
            Title = title,
            Summary = summary,
            Published = published,
            Source = "Playbook Mock Wire",
            Url = null,
            RelatedPlayerNames = players,
            RelatedTeamIds = teams,
            RelatedPlayerIds = [],
            Category = category,
            Priority = priority
        };
}
