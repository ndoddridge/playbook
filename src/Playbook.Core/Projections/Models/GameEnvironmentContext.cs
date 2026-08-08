namespace Playbook.Core.Projections.Models;

/// <summary>
/// Optional game-environment influence. Unavailable fields stay null — never invented.
/// </summary>
public sealed class GameEnvironmentContext
{
    public required bool IsAvailable { get; init; }

    public decimal? TeamImpliedTotal { get; init; }

    public decimal? OpponentImpliedTotal { get; init; }

    public decimal? ExpectedPace { get; init; }

    public decimal? ExpectedCompetitiveness { get; init; }

    public bool? IsHome { get; init; }

    public string? WeatherSummary { get; init; }

    public string? Summary { get; init; }

    public static GameEnvironmentContext Unavailable(string reason = "Game environment data not yet available") =>
        new()
        {
            IsAvailable = false,
            Summary = reason
        };
}
