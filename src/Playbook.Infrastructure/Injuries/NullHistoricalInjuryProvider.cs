using Playbook.Application.Injuries.Interfaces;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Placeholder historical provider. Live ESPN/Sleeper do not supply career injury history.
/// Swap in a real implementation behind this interface when a reliable source is available.
/// </summary>
public sealed class NullHistoricalInjuryProvider : IHistoricalInjuryProvider
{
    public HistoricalInjuryProviderKind Kind => HistoricalInjuryProviderKind.None;

    public string DisplayName => "None (not configured)";

    public bool IsConfigured => false;

    public Task<IReadOnlyList<PlayerInjuryRecord>> GetHistoricalInjuriesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlayerInjuryRecord>>([]);
}
