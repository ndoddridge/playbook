namespace Playbook.Application.Injuries;

public enum InjuryProviderKind
{
    Mock = 0,
    Live = 1
}

public sealed class InjuryOptions
{
    public const string SectionName = "Injuries";

    public InjuryProviderKind Provider { get; set; } = InjuryProviderKind.Live;

    public string CacheFileName { get; set; } = "player-injuries-cache.json";

    /// <summary>Reuse local injury history cache when younger than this many minutes.</summary>
    public int CacheTtlMinutes { get; set; } = 180;

    public int TimeoutSeconds { get; set; } = 60;
}
