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

/// <summary>Verification posture for injury knowledge.</summary>
public enum InjuryVerification
{
    /// <summary>Official / provider injury designation.</summary>
    Verified = 0,

    /// <summary>Reported in news or practice notes; not an official designation.</summary>
    Unconfirmed = 1,

    /// <summary>Historical record from a trusted historical provider.</summary>
    Historical = 2
}
