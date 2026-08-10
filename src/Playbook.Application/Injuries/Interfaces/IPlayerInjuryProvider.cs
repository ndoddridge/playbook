using Playbook.Core.Injuries.Models;

namespace Playbook.Application.Injuries.Interfaces;

/// <summary>
/// Source of normalized current injury designations (Mock or Live). UI never consumes this directly.
/// </summary>
public interface IPlayerInjuryProvider
{
    InjuryProviderKind Kind { get; }

    string DisplayName { get; }

    InjuryProviderCapabilities Capabilities { get; }

    Task<IReadOnlyList<PlayerInjuryRecord>> GetInjuriesAsync(
        CancellationToken cancellationToken = default);
}
