using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Knowledge;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;

namespace Playbook.Application.Predictions;

/// <summary>
/// All league-independent inputs for one Quick Pick evaluation.
/// Unavailable nested objects mean "missing" — never invent replacements.
/// </summary>
public sealed class QuickPickEvaluationContext
{
    public required PropLine Line { get; init; }

    public decimal? PlaybookProjection { get; init; }

    public int ProjectionConfidence { get; init; }

    public int Volatility { get; init; }

    public PlayerIntelligenceProfile? Intelligence { get; init; }

    public PlayerStatisticalContext? StatisticalContext { get; init; }

    public PlayerInjuryProfile? InjuryProfile { get; init; }

    /// <summary>
    /// Player's base roster status (Active/Suspended/InjuredReserve/PracticeSquad/...) — distinct
    /// from the week-to-week injury designation in <see cref="InjuryProfile"/>. Null when the
    /// line has no matched player.
    /// </summary>
    public PlayerStatus? RosterStatus { get; init; }

    public IReadOnlyList<IntelligenceFact> RecentFacts { get; init; } = [];

    /// <summary>Season phase of the game this pick belongs to.</summary>
    public NflSeasonPhase SeasonPhase { get; init; } = NflSeasonPhase.RegularSeason;

    /// <summary>
    /// True when the counting-stat baseline is prior regular-season production
    /// applied during a preseason slate (prior only — not current form).
    /// </summary>
    public bool UsingPriorRegularSeasonProduction { get; init; }

    public PlayerProductionSnapshot? Production { get; init; }

    /// <summary>
    /// Shared knowledge / PredictionContext substrate for this evaluation.
    /// Attached by the knowledge layer — does not replace Quick Pick scoring.
    /// </summary>
    public PredictionContext? PredictionContext { get; init; }
}
