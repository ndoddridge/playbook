namespace Playbook.Core.Intelligence.Models;

/// <summary>
/// Origin of an intelligence fact. Sources are football/data signals — not fantasy platforms.
/// </summary>
public enum IntelligenceSource
{
    Tracking = 0,
    Charting = 1,
    InjuryReport = 2,
    Weather = 3,
    Coaching = 4,
    BettingMarket = 5,
    DepthChart = 6,
    Historical = 7,
    Film = 8,
    News = 9
}
