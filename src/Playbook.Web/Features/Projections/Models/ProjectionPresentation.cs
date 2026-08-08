namespace Playbook.Web.Features.Projections.Models;

public static class ProjectionPresentation
{
    public static string Points(decimal value) => value.ToString("0.0");

    public static string Percent(int value) => $"{value}%";

    public static string RelativeTime(DateTimeOffset when)
    {
        var delta = DateTimeOffset.Now - when;
        if (delta.TotalSeconds < 60)
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

        return when.ToLocalTime().ToString("MMM d");
    }
}
