namespace Playbook.Core.Stats.Models;

/// <summary>
/// League-independent football counting statistics.
/// Null = missing/unknown. Zero = recorded zero. Do not coerce null to zero for scoring inputs
/// without an explicit policy — fantasy scoring treats null as contribute-nothing while preserving
/// the distinction on the source record.
/// </summary>
public sealed class CanonicalCountingStats
{
    public int? PassAttempts { get; init; }
    public int? PassCompletions { get; init; }
    public int? PassYards { get; init; }
    public int? PassTouchdowns { get; init; }
    public int? PassInterceptions { get; init; }

    public int? RushAttempts { get; init; }
    public int? RushYards { get; init; }
    public int? RushTouchdowns { get; init; }

    public int? Targets { get; init; }
    public int? Receptions { get; init; }
    public int? ReceivingYards { get; init; }
    public int? ReceivingTouchdowns { get; init; }

    public int? Fumbles { get; init; }

    /// <summary>K/DST structural placeholders for later detailed support.</summary>
    public int? FieldGoalsMade { get; init; }
    public int? ExtraPointsMade { get; init; }
    public int? DefensiveTouchdowns { get; init; }
    public int? Sacks { get; init; }
    public int? Safeties { get; init; }

    public IReadOnlyList<string> ListMissingCoreFields()
    {
        var missing = new List<string>();
        void Check(string name, int? value)
        {
            if (value is null)
            {
                missing.Add(name);
            }
        }

        Check(nameof(PassAttempts), PassAttempts);
        Check(nameof(PassCompletions), PassCompletions);
        Check(nameof(PassYards), PassYards);
        Check(nameof(PassTouchdowns), PassTouchdowns);
        Check(nameof(PassInterceptions), PassInterceptions);
        Check(nameof(RushAttempts), RushAttempts);
        Check(nameof(RushYards), RushYards);
        Check(nameof(RushTouchdowns), RushTouchdowns);
        Check(nameof(Targets), Targets);
        Check(nameof(Receptions), Receptions);
        Check(nameof(ReceivingYards), ReceivingYards);
        Check(nameof(ReceivingTouchdowns), ReceivingTouchdowns);
        Check(nameof(Fumbles), Fumbles);
        return missing;
    }
}
