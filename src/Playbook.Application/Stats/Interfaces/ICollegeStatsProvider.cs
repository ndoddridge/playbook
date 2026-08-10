using Playbook.Core.Stats.Models;

namespace Playbook.Application.Stats.Interfaces;

/// <summary>
/// Dedicated source of college season statistics. Isolated from the NFL stats provider.
/// Returns only <see cref="StatsPeriod.College"/> rows — never fabricates missing data.
/// </summary>
public interface ICollegeStatsProvider
{
    CollegeStatsProviderKind Kind { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<PlayerSeasonStats>> GetCollegeStatsAsync(
        CollegeStatsSyncRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CollegeStatsSyncRequest
{
    public required IReadOnlyList<CollegePlayerCandidate> Candidates { get; init; }
}

public sealed class CollegePlayerCandidate
{
    public required Guid PlayerId { get; init; }

    public required string FullName { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string? Team { get; init; }

    public string? College { get; init; }

    public int? YearsPro { get; init; }

    /// <summary>ESPN athlete id when already known (e.g. from Sleeper).</summary>
    public string? EspnAthleteId { get; init; }
}
