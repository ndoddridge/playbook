namespace Playbook.Core.Research;

/// <summary>
/// Classification of a graded prediction's expected-vs-actual gap. Purely descriptive evidence —
/// never used to automatically rewrite Projection V2, Confidence V2, or the Confidence-Aware
/// Decision Policy. Model changes stay controlled experiments; this enum only feeds permanent
/// research memory ("what did Playbook believe, what happened, why").
/// </summary>
public enum PredictionOutcomeClassification
{
    /// <summary>Actual landed close enough to the projection/line pick to count as a hit.</summary>
    Success,

    /// <summary>Meaningful miss with no other explanation on file — the projection itself was off.</summary>
    ProjectionError,

    /// <summary>Miss consistent with the player's role being different than assumed at snapshot time.</summary>
    RoleError,

    /// <summary>A known injury (current or newly reported) plausibly explains the miss.</summary>
    InjurySignal,

    /// <summary>Actual production far exceeded the snapshot's expectation — a role change worth tracking.</summary>
    MeaningfulRoleSignal,

    /// <summary>Actual outcome could not be retrieved from any available source.</summary>
    DataGap,

    /// <summary>Preseason miss within the elevated preseason noise band — not treated as signal.</summary>
    PreseasonNoise,

    /// <summary>Regular-season miss within the normal week-to-week variance band.</summary>
    RegularSeasonNoise
}
