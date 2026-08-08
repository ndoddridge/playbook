using Playbook.Core.Injuries.Models;

namespace Playbook.Application.Injuries.Interfaces;

public enum CollegeInjuryProviderKind
{
    None = 0,
    Mock = 1
    // Future: Live college injury sources.
}

/// <summary>
/// Optional college injury history. Live ESPN/Sleeper do not implement this.
/// </summary>
public interface ICollegeInjuryProvider
{
    CollegeInjuryProviderKind Kind { get; }

    string DisplayName { get; }

    bool IsConfigured { get; }

    Task<IReadOnlyList<PlayerInjuryRecord>> GetCollegeInjuriesAsync(
        CancellationToken cancellationToken = default);
}
