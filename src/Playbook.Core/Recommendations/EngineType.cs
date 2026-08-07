namespace Playbook.Core.Recommendations;

/// <summary>
/// Identifies which intelligence engine produced a recommendation.
/// </summary>
public enum EngineType
{
    Unknown = 0,
    Decision = 1,
    Projection = 2,
    Draft = 3,
    Waiver = 4,
    Trade = 5,
    Knowledge = 6,
    QuickPicks = 7
}
