namespace Playbook.Core.Draft;

/// <summary>Strategic role of a pick in the compact YOUR PICK slate — not a raw score rank.</summary>
public enum RecommendationRole
{
    Primary = 0,
    Alternative = 1,
    Upside = 2
}

public static class RecommendationRolePolicy
{
    public static string DisplayName(RecommendationRole role) => role switch
    {
        RecommendationRole.Primary => "Best Fit",
        RecommendationRole.Alternative => "Alternative",
        RecommendationRole.Upside => "Upside",
        _ => ""
    };

    public static string ShortLabel(RecommendationRole role) => role switch
    {
        RecommendationRole.Primary => "BEST FIT",
        RecommendationRole.Alternative => "ALTERNATIVE",
        RecommendationRole.Upside => "UPSIDE",
        _ => ""
    };
}
