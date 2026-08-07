namespace Playbook.Core.Intelligence.Models;

/// <summary>
/// Football-side classification for an <see cref="IntelligenceFact"/>.
/// Intentionally excludes fantasy categories (points, ranks, leagues).
/// </summary>
public enum IntelligenceCategory
{
    Usage = 0,
    Matchup = 1,
    Injury = 2,
    Weather = 3,
    Scheme = 4,
    Coaching = 5,
    Market = 6,
    Opportunity = 7,
    Efficiency = 8,
    Situation = 9,
    DepthChart = 10,
    Practice = 11,
    Transaction = 12,
    Suspension = 13,
    Contract = 14,
    GameEnvironment = 15,
    TeamChemistry = 16,
    General = 17
}
