namespace Playbook.Core.Research;

/// <summary>
/// What kind of thing a <see cref="PlayerEvidenceItem"/> is evidence of — derived directly from
/// <see cref="PredictionOutcomeClassification"/>, one graded research-memory snapshot at a time.
/// </summary>
public enum EvidenceType
{
    /// <summary>Playbook's projection was accurate — a mild positive signal about model fit.</summary>
    ProjectionAccuracy,

    /// <summary>A meaningful miss with no injury or magnitude explanation on file.</summary>
    ProjectionError,

    /// <summary>Actual production fell far short with no injury on file — role may differ from assumed.</summary>
    RoleConcern,

    /// <summary>Actual production far exceeded expectation — a possible expanded role worth tracking.</summary>
    MeaningfulRoleChange,

    /// <summary>A known injury plausibly explains the miss.</summary>
    InjurySignal,

    /// <summary>No actual outcome could be retrieved — an honest gap, not a fact about the player.</summary>
    ParticipationGap,

    /// <summary>Miss within the expected noise band for the season phase — not treated as signal.</summary>
    PhaseNoise
}
