namespace Playbook.Core.Intelligence.Models;

/// <summary>
/// Composed fantasy-team intelligence for the currently selected league + owned team.
/// Built from existing player assessments / projections — never invents engines or data.
/// </summary>
public sealed class FantasyTeamIntelligenceReport
{
    public required Guid? LeagueId { get; init; }

    public required int? SelectedRosterId { get; init; }

    public required string LeagueName { get; init; }

    public required string TeamName { get; init; }

    public required string LeagueTypeLabel { get; init; }

    public required string ScoringLabel { get; init; }

    public required int Week { get; init; }

    public required bool IsSetupComplete { get; init; }

    public required bool HasRosterPlayers { get; init; }

    public required string? RosterOutlookLabel { get; init; }

    public required string? RosterOutlookDetail { get; init; }

    public required IReadOnlyList<string> Strengths { get; init; }

    public required IReadOnlyList<string> Weaknesses { get; init; }

    public required IReadOnlyList<string> ImmediateConcerns { get; init; }

    public required IReadOnlyList<string> WhatMatters { get; init; }

    public required IReadOnlyList<StartSitRecommendation> StartSit { get; init; }

    public required IReadOnlyList<RosterPlayerIntelligence> RosterIntelligence { get; init; }

    public required IReadOnlyList<TeamRosterAlert> Alerts { get; init; }

    public required IReadOnlyList<string> UnavailableSignals { get; init; }

    public required string StatusMessage { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }
}

public sealed class StartSitRecommendation
{
    public required StartSitAction Action { get; init; }

    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required string PositionLabel { get; init; }

    public required string? ProjectionSummary { get; init; }

    public required int Confidence { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }

    public required bool InsufficientData { get; init; }
}

public enum StartSitAction
{
    Start = 0,
    Sit = 1
}

public sealed class RosterPlayerIntelligence
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required string PositionLabel { get; init; }

    public required bool IsStarter { get; init; }

    public required string OutlookLabel { get; init; }

    public required string? ProjectionSummary { get; init; }

    public required int? OpportunityScore { get; init; }

    public required int? UsageScore { get; init; }

    public required string HealthLabel { get; init; }

    public required int IntelligenceConfidence { get; init; }

    public required string? TrendLabel { get; init; }

    public required string? TopNews { get; init; }

    public required bool NewsConfirmed { get; init; }

    public required IReadOnlyList<string> KeySignals { get; init; }

    public required int Priority { get; init; }
}

public sealed class TeamRosterAlert
{
    public required string Title { get; init; }

    public required string Detail { get; init; }

    public required TeamAlertSeverity Severity { get; init; }

    public required Guid? PlayerId { get; init; }

    public required string Category { get; init; }
}

public enum TeamAlertSeverity
{
    Info = 0,
    Watch = 1,
    Urgent = 2
}
