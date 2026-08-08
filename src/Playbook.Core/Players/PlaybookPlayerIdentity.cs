namespace Playbook.Core.Players;

/// <summary>
/// Canonical crosswalk for one real athlete. PlaybookId is the app identity;
/// external IDs enable multi-provider joins without the Intelligence Engine knowing providers.
/// </summary>
public sealed class PlaybookPlayerIdentity
{
    public required Guid PlaybookId { get; init; }

    public required string FullName { get; init; }

    public string? Team { get; init; }

    public string? Position { get; init; }

    public string? SleeperId { get; init; }

    public string? EspnId { get; init; }

    public string? GsisId { get; init; }

    public string? YahooId { get; init; }

    public string? CollegeName { get; init; }

    public bool HasExternalIds =>
        !string.IsNullOrWhiteSpace(SleeperId) ||
        !string.IsNullOrWhiteSpace(EspnId) ||
        !string.IsNullOrWhiteSpace(GsisId);
}
