using Playbook.Core.Injuries.Models;

namespace Playbook.Application.Injuries.Interfaces;

/// <summary>
/// Source of normalized injury records (Mock or Live). UI never consumes this directly.
/// </summary>
public interface IPlayerInjuryProvider
{
    InjuryProviderKind Kind { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<PlayerInjuryRecord>> GetInjuriesAsync(
        CancellationToken cancellationToken = default);
}
