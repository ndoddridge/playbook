using Playbook.Core.Recommendations;

namespace Playbook.Tests;

public class RecommendationPresentationTests
{
    [Theory]
    [InlineData(RecommendationType.Start, "Start", "decision-card--start")]
    [InlineData(RecommendationType.QuickPick, "Quick Pick", "decision-card--quickpick")]
    [InlineData(RecommendationType.News, "News", "decision-card--news")]
    public void Action_Display_Is_Stable(RecommendationType action, string label, string css)
    {
        Assert.Equal(label, RecommendationPresentation.ActionLabel(action));
        Assert.Equal(css, RecommendationPresentation.ActionCss(action));
        Assert.False(string.IsNullOrWhiteSpace(RecommendationPresentation.ActionIcon(action)));
    }

    [Theory]
    [InlineData(-10, "0%")]
    [InlineData(50, "50%")]
    [InlineData(150, "100%")]
    public void Confidence_Is_Clamped(int confidence, string expected)
    {
        Assert.Equal(expected, RecommendationPresentation.ConfidenceLabel(confidence));
    }
}
