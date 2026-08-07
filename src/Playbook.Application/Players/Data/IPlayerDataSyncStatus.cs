namespace Playbook.Application.Players.Data;

/// <summary>
/// Developer-facing telemetry for the active player data provider.
/// </summary>
public interface IPlayerDataSyncStatus
{
    /// <summary>Configured provider from appsettings (Mock or Live).</summary>
    string ConfiguredProvider { get; }

    /// <summary>Provider actually serving data (may be Mock after fallback).</summary>
    string ActiveProvider { get; }

    DateTimeOffset? LastSuccessfulSync { get; }

    int PlayersLoaded { get; }

    TimeSpan? ProviderResponseTime { get; }

    string? LastError { get; }

    bool UsedFallback { get; }
}
