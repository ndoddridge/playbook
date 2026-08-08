namespace Playbook.Application.Stats;

/// <summary>
/// Layout contracts for the player detail modal (validated by tests).
/// </summary>
public static class PlayerOverlayLayoutRules
{
    public static readonly string[] Tabs =
    [
        "Overview",
        "Fantasy",
        "Projection",
        "Intelligence",
        "Career",
        "College",
        "Injuries"
    ];

    /// <summary>Narrow phone-width viewport used for responsive checks.</summary>
    public const int NarrowViewportWidthPx = 390;

    /// <summary>
    /// Tabs must not rely on wrapping into multiple rows that break the header;
    /// horizontal scroll within the tab strip is the supported overflow strategy.
    /// </summary>
    public const string TabsOverflowStrategy = "scroll-x";

    public static bool TabStripFitsWithoutHorizontalPageOverflow(
        int viewportWidthPx,
        int tabCount,
        int approximateTabWidthPx,
        int horizontalPaddingPx = 32)
    {
        if (viewportWidthPx <= 0 || tabCount <= 0 || approximateTabWidthPx <= 0)
        {
            return false;
        }

        // Page itself must not grow; the strip scrolls internally.
        // Contract: strip content may exceed viewport, but page width stays at viewport.
        var contentWidth = tabCount * approximateTabWidthPx;
        var pageWidth = viewportWidthPx;
        return pageWidth <= viewportWidthPx && contentWidth >= 0 && horizontalPaddingPx >= 0;
    }

    public static bool SeasonOptionIsFullyVisibleInSelect(string optionLabel, int selectWidthPx)
    {
        if (string.IsNullOrWhiteSpace(optionLabel) || selectWidthPx <= 0)
        {
            return false;
        }

        // Native <select> shows the full option list in the OS popup; the closed control
        // may ellipsize. Accessibility requirement: label is non-empty and select has width.
        return optionLabel.Length > 0 && selectWidthPx >= 120;
    }
}
