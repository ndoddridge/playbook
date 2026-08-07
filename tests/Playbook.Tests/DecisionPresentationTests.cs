using Playbook.Core.Decisions;

namespace Playbook.Tests;

public class DecisionPresentationTests
{
    [Theory]
    [InlineData(DecisionActionType.Start, "Start", "decision-card--start")]
    [InlineData(DecisionActionType.QuickPick, "Quick Pick", "decision-card--quickpick")]
    [InlineData(DecisionActionType.News, "News", "decision-card--news")]
    public void Action_Display_Is_Stable(DecisionActionType action, string label, string css)
    {
        Assert.Equal(label, DecisionPresentation.ActionLabel(action));
        Assert.Equal(css, DecisionPresentation.ActionCss(action));
        Assert.False(string.IsNullOrWhiteSpace(DecisionPresentation.ActionIcon(action)));
    }

    [Theory]
    [InlineData(-10, "0%")]
    [InlineData(50, "50%")]
    [InlineData(150, "100%")]
    public void Confidence_Is_Clamped(int confidence, string expected)
    {
        Assert.Equal(expected, DecisionPresentation.ConfidenceLabel(confidence));
    }
}
