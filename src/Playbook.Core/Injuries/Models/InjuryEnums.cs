namespace Playbook.Core.Injuries.Models;

/// <summary>Competition level for an injury record. Null/unknown stays unset — never assumed.</summary>
public enum InjuryCompetitionLevel
{
    Nfl = 0,
    College = 1
}

/// <summary>
/// Coarse severity when the source implies it. Missing severity stays unset.
/// </summary>
public enum InjurySeverity
{
    Minor = 0,
    Moderate = 1,
    Significant = 2,
    Major = 3
}

/// <summary>How strongly a historical injury should influence current evaluation.</summary>
public enum InjuryRelevanceBand
{
    /// <summary>Active or very recent significant injury.</summary>
    High = 0,

    /// <summary>Recent resolved or meaningful pattern.</summary>
    Moderate = 1,

    /// <summary>Older resolved injury — still shown, muted.</summary>
    Low = 2,

    /// <summary>Very old / isolated context — subtle emphasis only.</summary>
    Minimal = 3
}

/// <summary>Verification posture for injury knowledge. Never silently merge these.</summary>
public enum InjurySourceConfidence
{
    /// <summary>Directly provided by a reliable structured source (official report feed).</summary>
    Verified = 0,

    /// <summary>Supported by reliable news/reporting of a designation or practice status.</summary>
    Reported = 1,

    /// <summary>Possible injury buzz / speculation.</summary>
    Unconfirmed = 2,

    /// <summary>Insufficient information to classify.</summary>
    Unknown = 3
}
