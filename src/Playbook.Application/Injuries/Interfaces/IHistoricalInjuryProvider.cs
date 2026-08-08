using Playbook.Core.Injuries.Models;

namespace Playbook.Application.Injuries.Interfaces;

public enum HistoricalInjuryProviderKind
{
    /// <summary>No historical provider configured.</summary>
    None = 0,

    /// <summary>Mock historical seeds for tests / demos.</summary>
    Mock = 1,

    /// <summary>nflverse official-injury-report CSVs (2009+), free/public releases.</summary>
    Nflverse = 2
}

/// <summary>
/// Optional career/historical injury source. Isolated from the current-injury provider.
/// Live ESPN/Sleeper do not implement this — they are current-report only.
/// </summary>
public interface IHistoricalInjuryProvider
{
    HistoricalInjuryProviderKind Kind { get; }

    string DisplayName { get; }

    /// <summary>False when no historical source is configured.</summary>
    bool IsConfigured { get; }

    Task<IReadOnlyList<PlayerInjuryRecord>> GetHistoricalInjuriesAsync(
        CancellationToken cancellationToken = default);
}
