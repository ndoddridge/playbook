namespace Playbook.Core.Decisions;

/// <summary>
/// Distinguishes how much we can trust a signal or conclusion.
/// Critical for avoiding fabricated certainty and for future replay grading.
/// </summary>
public enum EvidenceStatus
{
    /// <summary>Reliable supporting data is present.</summary>
    Known = 0,

    /// <summary>We do not have enough information.</summary>
    Unknown = 1,

    /// <summary>Different signals point in different directions.</summary>
    Conflicting = 2,

    /// <summary>Some information exists, but evidence quality is insufficient for a strong conclusion.</summary>
    LowConfidence = 3
}
