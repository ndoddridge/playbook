namespace Playbook.Application.Leagues;

/// <summary>
/// Options bound from the <c>Leagues</c> configuration section.
/// </summary>
public sealed class LeagueOptions
{
    public const string SectionName = "Leagues";

    /// <summary>
    /// When true, the fixed demo leagues (Friends/Dynasty/Work League) are blended into
    /// <see cref="ILeagueService.GetAllLeagues"/> and used as the startup fallback league.
    /// Default true so local dev and the existing test suite keep working without a live
    /// connection. The deployed personal-use product sets this false — uploaded/connected
    /// leagues are the only source of truth there, and no mock team is auto-created.
    /// </summary>
    public bool EnableMockLeagues { get; set; } = true;
}
