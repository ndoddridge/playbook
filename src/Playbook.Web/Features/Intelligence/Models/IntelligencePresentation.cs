using Playbook.Core.Intelligence.Models;

namespace Playbook.Web.Features.Intelligence.Models;

/// <summary>
/// Display helpers only — no intelligence calculation.
/// </summary>
public static class IntelligencePresentation
{
    public static string Category(IntelligenceCategory category) => category switch
    {
        IntelligenceCategory.Usage => "Usage",
        IntelligenceCategory.Matchup => "Matchup",
        IntelligenceCategory.Injury => "Injury",
        IntelligenceCategory.Weather => "Weather",
        IntelligenceCategory.Scheme => "Scheme",
        IntelligenceCategory.Coaching => "Coaching",
        IntelligenceCategory.Market => "Market",
        IntelligenceCategory.Opportunity => "Opportunity",
        IntelligenceCategory.Efficiency => "Efficiency",
        IntelligenceCategory.Situation => "Situation",
        _ => category.ToString()
    };

    public static string Importance(IntelligenceImportance importance) => importance switch
    {
        IntelligenceImportance.Low => "Low",
        IntelligenceImportance.Medium => "Medium",
        IntelligenceImportance.High => "High",
        IntelligenceImportance.Critical => "Critical",
        _ => importance.ToString()
    };

    public static string ImportanceClass(IntelligenceImportance importance) => importance switch
    {
        IntelligenceImportance.Critical => "intel-importance--critical",
        IntelligenceImportance.High => "intel-importance--high",
        IntelligenceImportance.Medium => "intel-importance--medium",
        _ => "intel-importance--low"
    };

    public static string Source(IntelligenceSource source) => source switch
    {
        IntelligenceSource.Tracking => "Tracking",
        IntelligenceSource.Charting => "Charting",
        IntelligenceSource.InjuryReport => "Injury Report",
        IntelligenceSource.Weather => "Weather",
        IntelligenceSource.Coaching => "Coaching",
        IntelligenceSource.BettingMarket => "Betting Market",
        IntelligenceSource.DepthChart => "Depth Chart",
        IntelligenceSource.Historical => "Historical",
        IntelligenceSource.Film => "Film",
        IntelligenceSource.News => "News",
        _ => source.ToString()
    };

    public static string RelativeTime(DateTimeOffset created)
    {
        var delta = DateTimeOffset.UtcNow - created;
        if (delta.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)delta.TotalMinutes)}m ago";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        return $"{(int)delta.TotalDays}d ago";
    }
}
