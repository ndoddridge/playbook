namespace Playbook.Application.Players.Data;

/// <summary>
/// Options bound from the <c>PlayerData</c> configuration section.
/// </summary>
public sealed class PlayerDataOptions
{
    public const string SectionName = "PlayerData";

    /// <summary>
    /// <see cref="PlayerDataProviderKind.Mock"/> or <see cref="PlayerDataProviderKind.Live"/>.
    /// </summary>
    public PlayerDataProviderKind Provider { get; set; } = PlayerDataProviderKind.Mock;

    public SleeperOptions Sleeper { get; set; } = new();
}

/// <summary>
/// Sleeper NFL API settings. Auth is optional today (public read API) but isolated for future keys.
/// </summary>
public sealed class SleeperOptions
{
    public string BaseUrl { get; set; } = "https://api.sleeper.app/v1/";

    /// <summary>
    /// Reserved for future authenticated Sleeper / partner APIs. Unused for public player reads.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// HTTP timeout for live player catalog requests.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
