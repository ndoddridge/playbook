using Playbook.Core.News;

namespace Playbook.Web.Features.News.Models;

public static class NewsPresentation
{
    public static string Priority(NewsPriority priority) => priority switch
    {
        NewsPriority.Critical => "Critical",
        NewsPriority.High => "High",
        NewsPriority.Normal => "Normal",
        NewsPriority.Low => "Low",
        _ => priority.ToString()
    };

    public static string PriorityClass(NewsPriority priority) => priority switch
    {
        NewsPriority.Critical => "pb-badge pb-badge--danger",
        NewsPriority.High => "pb-badge pb-badge--warning",
        NewsPriority.Normal => "pb-badge pb-badge--accent",
        _ => "pb-badge"
    };

    public static string RelativeTime(DateTimeOffset published)
    {
        var delta = DateTimeOffset.Now - published;
        if (delta.TotalMinutes < 1)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        return published.LocalDateTime.ToString("MMM d");
    }
}
