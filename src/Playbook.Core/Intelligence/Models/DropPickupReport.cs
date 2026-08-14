namespace Playbook.Core.Intelligence.Models;

/// <summary>
/// Drop/Pickup intelligence for the currently selected league + owned team. Additive to
/// Start/Sit and Quick Picks — built entirely from existing projection/roster data, shares no
/// mutable state with the decision engine, and never fabricates ownership, waiver priority,
/// betting lines, news, or matchup signal.
/// </summary>
public sealed class DropPickupReport
{
    public required Guid? LeagueId { get; init; }

    public required int? SelectedRosterId { get; init; }

    public required string LeagueName { get; init; }

    public required string TeamName { get; init; }

    public required bool IsSetupComplete { get; init; }

    public required bool HasRosterPlayers { get; init; }

    /// <summary>Configured roster size (see <c>League.RosterLimit</c>); null when unknown (mock leagues).</summary>
    public required int? RosterLimit { get; init; }

    /// <summary>Roster spots counted against the limit (taxi squad excluded).</summary>
    public required int RosterCount { get; init; }

    /// <summary>Available (unrostered, league-wide) players considered as pickup candidates.</summary>
    public required int AvailablePlayerCount { get; init; }

    /// <summary>
    /// Ranked drop→pickup swap suggestions (best value gain first). Every suggestion is a
    /// same-position 1-for-1 swap, so the roster count never changes.
    /// </summary>
    public required IReadOnlyList<DropPickupSuggestion> Suggestions { get; init; }

    /// <summary>
    /// Every roster player evaluated as a keep/drop candidate (not just those with a same-position
    /// swap in <see cref="Suggestions"/>), ranked most-expendable last. Carries the
    /// ImmediateValue/DynastyValue breakdown so a classification can be inspected, not just trusted.
    /// </summary>
    public required IReadOnlyList<DropCandidate> RosterAssessment { get; init; }

    public required IReadOnlyList<string> UnavailableSignals { get; init; }

    public required string StatusMessage { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>One recommended same-position roster swap.</summary>
public sealed class DropPickupSuggestion
{
    public required DropCandidate Drop { get; init; }

    public required PickupCandidate Pickup { get; init; }

    /// <summary>Pickup projected points minus Drop projected points (0 when either is unknown).</summary>
    public required double ValueGain { get; init; }

    public required string Reasoning { get; init; }
}

/// <summary>
/// Rough keep/drop lean for a roster player. Derived only from ImmediateValue/DynastyValue
/// thresholds on existing data — NOT a real trade-market assessment (no trade value is modeled
/// yet), so "Trade" here means "meaningful value but positionally replaceable," not an appraised
/// asset price.
/// </summary>
public enum DropPickupClassification
{
    Hold,
    Trade,
    Drop
}

/// <summary>A current roster player evaluated as a candidate to drop.</summary>
public sealed class DropCandidate
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required string PositionLabel { get; init; }

    public required bool IsStarter { get; init; }

    public required double? ProjectedPoints { get; init; }

    public required int? Confidence { get; init; }

    /// <summary>
    /// Final ranking score used to sort drop candidates. Equal to <see cref="ImmediateValue"/>
    /// for Redraft/Keeper leagues; for Dynasty leagues it blends a dampened ImmediateValue with
    /// the full <see cref="DynastyValue"/> so a single week's projection swing cannot by itself
    /// overwhelm long-horizon value. Lower = more expendable. Never shown as a bare number to
    /// avoid implying false precision — see <see cref="ScoreBreakdown"/> and <see cref="Reasons"/>.
    /// </summary>
    public required double KeepValueScore { get; init; }

    /// <summary>
    /// Short-horizon roster-construction value: this week's replacement margin, projection
    /// confidence, positional scarcity, and current starter status. Same formula regardless of
    /// league type.
    /// </summary>
    public required double ImmediateValue { get; init; }

    /// <summary>
    /// Long-horizon value from age, current role, injury trajectory, positional scarcity, and
    /// waiver-replaceability — deliberately excludes raw projected points so a single week's
    /// production cannot dominate it. Null for Redraft/Keeper leagues (dynasty weighting is
    /// Dynasty-only). Missing inputs (no known age, no injury on file) contribute 0, not a penalty.
    /// </summary>
    public required double? DynastyValue { get; init; }

    /// <summary>
    /// Heuristic lean derived from <see cref="KeepValueScore"/> — see <see cref="DropPickupClassification"/>
    /// for what "Trade" does and doesn't mean here.
    /// </summary>
    public required DropPickupClassification Classification { get; init; }

    /// <summary>Human-readable line(s) showing how ImmediateValue/DynastyValue were composed.</summary>
    public required IReadOnlyList<string> ScoreBreakdown { get; init; }

    /// <summary>Own projection minus the best available same-position free agent's projection.</summary>
    public required double? ReplacementMargin { get; init; }

    public required int PositionDepthOnRoster { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }
}

/// <summary>An available (unrostered, league-wide) player evaluated as a pickup candidate.</summary>
public sealed class PickupCandidate
{
    public required Guid PlayerId { get; init; }

    public required string PlayerName { get; init; }

    public required string PositionLabel { get; init; }

    public required double? ProjectedPoints { get; init; }

    public required int? Confidence { get; init; }

    public required double PickupValueScore { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }
}
