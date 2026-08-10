namespace Playbook.Application.Injuries;

/// <summary>
/// Declared capabilities of an injury provider. Callers must not assume unsupported fields.
/// </summary>
public sealed class InjuryProviderCapabilities
{
    public required bool SupportsCurrentInjuries { get; init; }

    public required bool SupportsHistoricalInjuries { get; init; }

    public bool SupportsPracticeStatus { get; init; }

    public bool SupportsGameStatus { get; init; }

    public bool SupportsBodyPart { get; init; }

    public bool SupportsInjuryNotes { get; init; }

    public bool SupportsSourceUrls { get; init; }

    /// <summary>Human-readable summary of what the live feed actually returns.</summary>
    public string? Notes { get; init; }

    public static InjuryProviderCapabilities CurrentOnlyEspnSleeper { get; } = new()
    {
        SupportsCurrentInjuries = true,
        SupportsHistoricalInjuries = false,
        SupportsPracticeStatus = true, // Sleeper enrichment when present; often sparse
        SupportsGameStatus = true,     // Derived from ESPN/Sleeper status designation
        SupportsBodyPart = true,       // Sleeper field and/or parenthetical in ESPN comments
        SupportsInjuryNotes = true,
        SupportsSourceUrls = true,
        Notes =
            "ESPN NFL injuries feed is a current-report snapshot (roughly in-season report window). " +
            "It does not supply career historical injury records. " +
            "Sleeper supplies current injury_status / practice fields when set."
    };

    public static InjuryProviderCapabilities MockCurrentOnly { get; } = new()
    {
        SupportsCurrentInjuries = true,
        SupportsHistoricalInjuries = false,
        SupportsPracticeStatus = true,
        SupportsGameStatus = true,
        SupportsBodyPart = true,
        SupportsInjuryNotes = true,
        SupportsSourceUrls = false,
        Notes =
            "Mock current designations only. Historical rows come from MockHistoricalInjuryProvider " +
            "when Injuries:Provider=Mock — never fabricated from current-report snapshots."
    };

    /// <summary>Combined mock stack (current + historical providers) for cache documents that include history.</summary>
    public static InjuryProviderCapabilities MockWithHistory { get; } = new()
    {
        SupportsCurrentInjuries = true,
        SupportsHistoricalInjuries = true,
        SupportsPracticeStatus = true,
        SupportsGameStatus = true,
        SupportsBodyPart = true,
        SupportsInjuryNotes = true,
        SupportsSourceUrls = false,
        Notes = "Mock current + MockHistoricalInjuryProvider seeds for selected catalog players."
    };
}
