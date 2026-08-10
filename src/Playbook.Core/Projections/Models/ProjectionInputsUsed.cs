namespace Playbook.Core.Projections.Models;

/// <summary>
/// Provenance of inputs that contributed to a projection. Optional inputs may be unavailable.
/// </summary>
public sealed class ProjectionInputsUsed
{
    public bool HistoricalStatistics { get; init; }

    public bool RecentUsage { get; init; }

    public bool CareerBaseline { get; init; }

    public bool CollegeStatistics { get; init; }

    public bool IntelligenceProfile { get; init; }

    public bool InjurySignal { get; init; }

    public bool MatchupContext { get; init; }

    public bool GameEnvironment { get; init; }

    public bool LeagueScoring { get; init; }

    public string ProductionSource { get; init; } = string.Empty;

    public string BaselineMethod { get; init; } = string.Empty;

    public IReadOnlyList<string> Notes { get; init; } = [];

    public IReadOnlyList<string> UnavailableInputs { get; init; } = [];
}
