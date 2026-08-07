namespace Playbook.Core.Recommendations;

/// <summary>
/// Recommended action the user can take. Engines emit these; UI only displays them.
/// </summary>
public enum RecommendationType
{
    Start = 0,
    Bench = 1,
    Trade = 2,
    Waiver = 3,
    Add = 4,
    Drop = 5,
    Hold = 6,
    Draft = 7,
    QuickPick = 8,
    News = 9
}
