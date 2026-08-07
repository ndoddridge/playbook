namespace Playbook.Core.Decisions;

/// <summary>
/// Pure presentation labels for decision surfaces. No engine logic.
/// </summary>
public static class DecisionPresentation
{
    public static string ActionLabel(DecisionActionType actionType) => actionType switch
    {
        DecisionActionType.Start => "Start",
        DecisionActionType.Bench => "Bench",
        DecisionActionType.Trade => "Trade",
        DecisionActionType.Waiver => "Waiver",
        DecisionActionType.Add => "Add",
        DecisionActionType.Drop => "Drop",
        DecisionActionType.Hold => "Hold",
        DecisionActionType.Draft => "Draft",
        DecisionActionType.QuickPick => "Quick Pick",
        DecisionActionType.News => "News",
        _ => actionType.ToString()
    };

    public static string ActionIcon(DecisionActionType actionType) => actionType switch
    {
        DecisionActionType.Start => "▶",
        DecisionActionType.Bench => "❚❚",
        DecisionActionType.Trade => "⇄",
        DecisionActionType.Waiver => "⚡",
        DecisionActionType.Add => "+",
        DecisionActionType.Drop => "−",
        DecisionActionType.Hold => "◎",
        DecisionActionType.Draft => "↑",
        DecisionActionType.QuickPick => "★",
        DecisionActionType.News => "▣",
        _ => "•"
    };

    public static string ActionCss(DecisionActionType actionType) =>
        $"decision-card--{actionType.ToString().ToLowerInvariant()}";

    public static string PriorityLabel(DecisionPriority priority) => priority.ToString();

    public static string PriorityCss(DecisionPriority priority) =>
        $"decision-priority--{priority.ToString().ToLowerInvariant()}";

    public static string StatusLabel(DecisionStatus status) => status.ToString();

    public static string ConfidenceLabel(int confidence) =>
        $"{Math.Clamp(confidence, 0, 100)}%";

    public static string TimestampLabel(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("MMM d · h:mm tt");
}
