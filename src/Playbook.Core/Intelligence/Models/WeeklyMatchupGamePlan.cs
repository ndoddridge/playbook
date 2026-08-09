namespace Playbook.Core.Intelligence.Models;

/// <summary>
/// Weekly matchup / game-plan view for the currently selected league + owned team.
/// Composed from existing projections and team intelligence — never invents scores.
/// </summary>
public sealed class WeeklyMatchupGamePlan
{
    public required Guid? LeagueId { get; init; }

    public required int? SelectedRosterId { get; init; }

    public required int? OpponentRosterId { get; init; }

    public required string LeagueName { get; init; }

    public required int Week { get; init; }

    public required string ScoringLabel { get; init; }

    public required bool IsSetupComplete { get; init; }

    public required bool HasMatchup { get; init; }

    public required string MyTeamName { get; init; }

    public required string OpponentTeamName { get; init; }

    public required MatchupOpponentSource OpponentSource { get; init; }

    public required decimal? MyProjectedScore { get; init; }

    public required decimal? OpponentProjectedScore { get; init; }

    public required decimal? ProjectionDifference { get; init; }

    public required MatchupAssessment Assessment { get; init; }

    public required string AssessmentLabel { get; init; }

    public required string AssessmentSummary { get; init; }

    public required int MatchupConfidence { get; init; }

    public required string ConfidenceNote { get; init; }

    public required MatchupVolatility Volatility { get; init; }

    public required string VolatilityLabel { get; init; }

    public required string AdvantageLabel { get; init; }

    public required IReadOnlyList<MatchupSwing> KeySwings { get; init; }

    public required IReadOnlyList<MatchupFactor> BiggestRisks { get; init; }

    public required IReadOnlyList<MatchupFactor> BiggestAdvantages { get; init; }

    public required IReadOnlyList<MatchupLineupImpact> LineupImpact { get; init; }

    public required OpponentScoutReport OpponentScout { get; init; }

    public required IReadOnlyList<string> UnavailableSignals { get; init; }

    public required string StatusMessage { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }
}

public enum MatchupOpponentSource
{
    None = 0,
    /// <summary>Deterministic pairing from other league teams when a live H2H feed is unavailable.</summary>
    DerivedDemo = 1,
    SleeperMatchup = 2
}

public enum MatchupAssessment
{
    Unknown = 0,
    Favorable = 1,
    Competitive = 2,
    Challenging = 3
}

public enum MatchupVolatility
{
    Unknown = 0,
    Stable = 1,
    Mixed = 2,
    Volatile = 3
}

public sealed class MatchupSwing
{
    public required string Title { get; init; }

    public required string Detail { get; init; }

    public required string SideLabel { get; init; }

    public required Guid? PlayerId { get; init; }

    public required string Category { get; init; }
}

public sealed class MatchupFactor
{
    public required string Title { get; init; }

    public required string Detail { get; init; }

    public required Guid? PlayerId { get; init; }
}

public sealed class MatchupLineupImpact
{
    public required StartSitAction Action { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required string PositionLabel { get; init; }

    public required string? ProjectionSummary { get; init; }

    public required int Confidence { get; init; }

    public required bool InsufficientData { get; init; }

    public required string MatchupRelevance { get; init; }

    public required string IfStarted { get; init; }

    public required string IfSat { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }
}

public sealed class OpponentScoutReport
{
    public required string TeamName { get; init; }

    public required IReadOnlyList<string> Strengths { get; init; }

    public required IReadOnlyList<string> Weaknesses { get; init; }

    public required IReadOnlyList<MatchupFactor> BiggestThreats { get; init; }

    public required IReadOnlyList<MatchupFactor> RelevantNews { get; init; }

    public required IReadOnlyList<MatchupSwingPlayer> SwingPlayers { get; init; }

    public required string StatusMessage { get; init; }
}

public sealed class MatchupSwingPlayer
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required string PositionLabel { get; init; }

    public required string? ProjectionSummary { get; init; }

    public required string Note { get; init; }
}
