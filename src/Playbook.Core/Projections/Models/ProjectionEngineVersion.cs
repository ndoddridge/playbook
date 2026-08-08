namespace Playbook.Core.Projections.Models;

/// <summary>
/// Version stamp for projection outputs — enables Replay Lab / backtesting across model generations.
/// </summary>
public static class ProjectionEngineVersions
{
    /// <summary>First real explainable Projection Engine.</summary>
    public const string V0_1 = "0.1";

    public const string Current = V0_1;

    public const string DisplayName = "Projection Engine v0.1";
}
