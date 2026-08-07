namespace Playbook.Core.Recommendations;

/// <summary>
/// Pure presentation labels for recommendation surfaces. No engine logic.
/// </summary>
public static class RecommendationPresentation
{
    public static string ActionLabel(RecommendationType actionType) => actionType switch
    {
        RecommendationType.Start => "Start",
        RecommendationType.Bench => "Bench",
        RecommendationType.Trade => "Trade",
        RecommendationType.Waiver => "Waiver",
        RecommendationType.Add => "Add",
        RecommendationType.Drop => "Drop",
        RecommendationType.Hold => "Hold",
        RecommendationType.Draft => "Draft",
        RecommendationType.QuickPick => "Quick Pick",
        RecommendationType.News => "News",
        _ => actionType.ToString()
    };

    public static string ActionIcon(RecommendationType actionType) => actionType switch
    {
        RecommendationType.Start => "▶",
        RecommendationType.Bench => "❚❚",
        RecommendationType.Trade => "⇄",
        RecommendationType.Waiver => "⚡",
        RecommendationType.Add => "+",
        RecommendationType.Drop => "−",
        RecommendationType.Hold => "◎",
        RecommendationType.Draft => "↑",
        RecommendationType.QuickPick => "★",
        RecommendationType.News => "▣",
        _ => "•"
    };

    public static string ActionCss(RecommendationType actionType) =>
        $"decision-card--{actionType.ToString().ToLowerInvariant()}";

    public static string PriorityLabel(RecommendationPriority priority) => priority.ToString();

    public static string PriorityCss(RecommendationPriority priority) =>
        $"decision-priority--{priority.ToString().ToLowerInvariant()}";

    public static string StatusLabel(RecommendationStatus status) => status.ToString();

    public static string CategoryLabel(RecommendationCategory category) => category switch
    {
        RecommendationCategory.Lineup => "Lineup",
        RecommendationCategory.Roster => "Roster",
        RecommendationCategory.Waivers => "Waivers",
        RecommendationCategory.Trades => "Trades",
        RecommendationCategory.Draft => "Draft",
        RecommendationCategory.QuickPicks => "Quick Picks",
        RecommendationCategory.News => "News",
        RecommendationCategory.General => "General",
        _ => category.ToString()
    };

    public static string ConfidenceLabel(int confidence) =>
        $"{Math.Clamp(confidence, 0, 100)}%";

    public static string TimestampLabel(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("MMM d · h:mm tt");
}
