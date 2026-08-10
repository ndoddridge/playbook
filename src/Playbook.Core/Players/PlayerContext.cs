using Playbook.Core.Leagues;

namespace Playbook.Core.Players;

/// <summary>
/// League-aware player view for overlays and engines.
/// Fantasy values come from services — UI never calculates them.
/// </summary>
public sealed class PlayerContext
{
    public required Player Player { get; init; }

    public League? League { get; init; }

    /// <summary>Fantasy roster that currently owns this player in the selected league, when known.</summary>
    public FantasyTeam? FantasyTeam { get; init; }

    public required ScoringType ScoringType { get; init; }

    public required decimal WeeklyProjection { get; init; }

    public required int RestOfSeasonRank { get; init; }

    public required int PositionalRank { get; init; }

    public required decimal ValueOverReplacement { get; init; }

    public required string RecommendationSummary { get; init; }

    public required int Confidence { get; init; }

    public PlayerProfile? Profile { get; init; }
}
