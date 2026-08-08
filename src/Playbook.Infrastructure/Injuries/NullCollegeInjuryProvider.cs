using Playbook.Application.Injuries.Interfaces;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Placeholder college injury provider. Live ESPN/Sleeper do not supply college injury history.
/// </summary>
public sealed class NullCollegeInjuryProvider : ICollegeInjuryProvider
{
    public CollegeInjuryProviderKind Kind => CollegeInjuryProviderKind.None;

    public string DisplayName => "None (not configured)";

    public bool IsConfigured => false;

    public Task<IReadOnlyList<PlayerInjuryRecord>> GetCollegeInjuriesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlayerInjuryRecord>>([]);
}
