using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Predictions;
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

    public IReadOnlyList<IntelligenceFact> RecentFacts { get; init; } = [];
}
