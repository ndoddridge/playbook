using Playbook.Application.Injuries.Interfaces;

namespace Playbook.Application.Injuries;

public enum InjuryProviderKind
{
    Mock = 0,
    Live = 1
}

public sealed class InjuryOptions
{
    public const string SectionName = "Injuries";

    /// <summary>Current-injury provider (ESPN+Sleeper Live, or Mock).</summary>
    public InjuryProviderKind Provider { get; set; } = InjuryProviderKind.Live;

    /// <summary>
    /// Historical NFL injury provider. Independent from <see cref="Provider"/>.
    /// Use Nflverse for real historical reports when current provider is Live.
    /// </summary>
    public HistoricalInjuryProviderKind HistoricalProvider { get; set; } = HistoricalInjuryProviderKind.Nflverse;

    /// <summary>How many recent NFL seasons of nflverse injury CSVs to load (inclusive of current).</summary>
    public int HistoricalSeasonCount { get; set; } = 8;

    public string CacheFileName { get; set; } = "player-injuries-cache.json";

    /// <summary>Reuse local injury history cache when younger than this many minutes.</summary>
    public int CacheTtlMinutes { get; set; } = 180;

    public int TimeoutSeconds { get; set; } = 90;
}
