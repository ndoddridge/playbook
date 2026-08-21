using Playbook.Core.Players;

namespace Playbook.Core.Draft;

/// <summary>
/// Live strategy snapshot rebuilt every recommendation cycle from the CURRENT roster + plan.
/// Intent only — the decision engine makes the final choice.
/// </summary>
public sealed class DraftStrategyState
{
    public required int Round { get; init; }

    public required int PickNumber { get; init; }

    public required DraftPhase Phase { get; init; }

    public required string StrategyPhaseLabel { get; init; }

    public required IReadOnlyDictionary<string, int> PositionalCounts { get; init; }

    public required IReadOnlyList<string> TargetPositions { get; init; }

    public required IReadOnlyList<string> HardPositions { get; init; }

    public required IReadOnlyList<string> PreferredTargetNames { get; init; }

    public required IReadOnlyList<string> ActiveFades { get; init; }

    public required int IrStashCount { get; init; }

    public required bool SkipKickerAndDst { get; init; }

    public required bool HerbertRostered { get; init; }

    public required bool PreferKylerLate { get; init; }

    public required string IntentSummary { get; init; }

    /// <summary>Current roster player names — used for conditionals (e.g. Ladd → Johnston).</summary>
    public required IReadOnlySet<string> RosteredPlayerNames { get; init; }

    public required DraftStrategyPlan Plan { get; init; }

    public static DraftStrategyState Build(
        DraftStrategyPlan plan,
        int round,
        int pickNumber,
        DraftPhase phase,
        IReadOnlyDictionary<string, int> positionalCounts,
        IReadOnlyList<Player> rosterPlayers)
    {
        var intent = plan.PhaseIntents.FirstOrDefault(p => round >= p.FromRound && round <= p.ToRound)
                     ?? plan.PhaseIntents.LastOrDefault()
                     ?? new PhaseIntent(1, 99, ["RB", "WR"], "Best available construction.");

        var irCount = CountIrStashes(rosterPlayers, plan);
        var skipKd = irCount >= plan.IrStrategy.MaxIrStashesToSkipKAndDst;
        var herbert = rosterPlayers.Any(p =>
            NamesMatch(p.FullName, plan.SpecialLateRules.HerbertPlayerName));
        var preferKyler = herbert && round >= plan.SpecialLateRules.LateRoundStart;
        var rosteredNames = new HashSet<string>(
            rosterPlayers
                .Select(p => p.FullName?.Trim() ?? string.Empty)
                .Where(n => n.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        // Soft targets from preferences still available — not hard locks.
        var preferred = plan.PreferredPlayers
            .Where(p => intent.PreferredPositions.Count == 0
                        || p.PositionHint is null
                        || intent.PreferredPositions.Contains(p.PositionHint, StringComparer.OrdinalIgnoreCase))
            .Select(p => p.PlayerName)
            .ToList();

        return new DraftStrategyState
        {
            Round = round,
            PickNumber = pickNumber,
            Phase = phase,
            StrategyPhaseLabel = $"{intent.FromRound}-{intent.ToRound}",
            PositionalCounts = positionalCounts,
            TargetPositions = intent.PreferredPositions,
            HardPositions = [], // Intent is soft; hard constraints are rare and empty by default.
            PreferredTargetNames = preferred,
            ActiveFades = plan.Fades,
            IrStashCount = irCount,
            SkipKickerAndDst = skipKd,
            HerbertRostered = herbert,
            PreferKylerLate = preferKyler,
            IntentSummary = intent.Intent,
            RosteredPlayerNames = rosteredNames,
            Plan = plan
        };
    }

    private static int CountIrStashes(IReadOnlyList<Player> roster, DraftStrategyPlan plan)
    {
        // Count rostered players that match configured IR targets — not invented IR designations.
        return roster.Count(p =>
            plan.IrStrategy.IrTargetNames.Any(n => NamesMatch(p.FullName, n)));
    }

    public static bool NamesMatch(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
