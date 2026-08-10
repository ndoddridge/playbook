namespace Playbook.Core.Replay;

/// <summary>
/// Controls which players enter historical evaluation snapshots.
/// Does not change projection/decision/confidence formulas or production KnowledgeMode.
/// </summary>
public enum HistoricalCandidateUniverse
{
    /// <summary>
    /// Reconstructed fantasy lab roster only (~QB3 / RB5 / WR6 / TE3).
    /// Default for frozen 2018 benchmark locks.
    /// </summary>
    LabRoster = 0,

    /// <summary>
    /// Broader cutoff-safe ACT skill-player universe reconstructible from nflverse.
    /// Players = all week-W ACT skill identities; Start/Sit roster = all of those
    /// players with starter flags from the same top-N-by-prior-PPG rule (uncapped bench).
    /// </summary>
    ExpandedSkillUniverse = 1
}

/// <summary>Why a player-week was excluded from a coverage slice.</summary>
public enum HistoricalCoverageExclusionReason
{
    NonSkillPosition = 0,
    NonActiveRosterStatus = 1,
    OutsideLabRosterCap = 2,
    NoPriorRegGames = 3,
    NoValidProjection = 4,
    NoWeekOutcome = 5,
    NoPositiveMarketProjection = 6
}
