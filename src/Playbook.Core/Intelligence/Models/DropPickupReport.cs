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
    /// Composite roster-keep score (projection + confidence + replacement margin + positional
    /// depth, plus dynasty age/early-career adjustment when applicable). Lower = more
    /// expendable. Used only to rank drop candidates, never shown as a bare number to avoid
    /// implying false precision — see <see cref="Reasons"/> instead.
    /// </summary>
    public required double KeepValueScore { get; init; }

    /// <summary>
    /// Dynasty-only Keep Value adjustment from age / years-pro / related youth signals
    /// (0 in redraft or when those fields are absent). Used to gate small weekly upgrades.
    /// </summary>
    public double DynastyKeepAdjustment { get; init; }

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
