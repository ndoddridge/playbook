namespace Playbook.Core.Predictions.Models;

/// <summary>
/// Team-level game scoring projection for Quick Picks game markets.
/// Used to estimate what a team will score in an upcoming game.
/// Preseason may return unavailable (insufficient player data/lineup certainty).
/// Regular season uses player-level aggregation for defensible estimates.
/// </summary>
public sealed class TeamGameProjection
{
    /// <summary>Team abbreviation (e.g., "BUF").</summary>
    public required string TeamAbbreviation { get; init; }

    /// <summary>Estimated points the team will score in the game.</summary>
    public required decimal EstimatedTeamScore { get; init; }

    /// <summary>Confidence in this estimate (0-100).</summary>
    public required int Confidence { get; init; }

    /// <summary>Volatility/uncertainty (0-100).</summary>
    public required int Volatility { get; init; }

    /// <summary>Human-readable explanation of how the estimate was derived.</summary>
    public required string Reasoning { get; init; }

    /// <summary>When null, insufficient data exists for a projection (preseason phase).</summary>
    public static TeamGameProjection Unavailable(string reason = "Team projection data not available for this phase") =>
        new()
        {
            TeamAbbreviation = "",
            EstimatedTeamScore = 0,
            Confidence = 0,
            Volatility = 100,
            Reasoning = reason
        };
}
