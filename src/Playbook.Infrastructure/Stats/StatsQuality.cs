using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

internal static class StatsQuality
{
    public static (StatsCompleteness Completeness, IReadOnlyList<string> Missing) Evaluate(
        CanonicalCountingStats stats,
        string? position)
    {
        var missing = new List<string>();
        var pos = (position ?? string.Empty).Trim().ToUpperInvariant();

        void Require(string name, int? value)
        {
            if (value is null)
            {
                missing.Add(name);
            }
        }

        switch (pos)
        {
            case "QB":
                Require(nameof(stats.PassAttempts), stats.PassAttempts);
                Require(nameof(stats.PassCompletions), stats.PassCompletions);
                Require(nameof(stats.PassYards), stats.PassYards);
                Require(nameof(stats.PassTouchdowns), stats.PassTouchdowns);
                Require(nameof(stats.PassInterceptions), stats.PassInterceptions);
                Require(nameof(stats.RushAttempts), stats.RushAttempts);
                Require(nameof(stats.RushYards), stats.RushYards);
                Require(nameof(stats.RushTouchdowns), stats.RushTouchdowns);
                Require(nameof(stats.Fumbles), stats.Fumbles);
                break;
            case "RB":
                Require(nameof(stats.RushAttempts), stats.RushAttempts);
                Require(nameof(stats.RushYards), stats.RushYards);
                Require(nameof(stats.RushTouchdowns), stats.RushTouchdowns);
                Require(nameof(stats.Targets), stats.Targets);
                Require(nameof(stats.Receptions), stats.Receptions);
                Require(nameof(stats.ReceivingYards), stats.ReceivingYards);
                Require(nameof(stats.ReceivingTouchdowns), stats.ReceivingTouchdowns);
                Require(nameof(stats.Fumbles), stats.Fumbles);
                break;
            case "WR":
            case "TE":
                Require(nameof(stats.Targets), stats.Targets);
                Require(nameof(stats.Receptions), stats.Receptions);
                Require(nameof(stats.ReceivingYards), stats.ReceivingYards);
                Require(nameof(stats.ReceivingTouchdowns), stats.ReceivingTouchdowns);
                Require(nameof(stats.RushAttempts), stats.RushAttempts);
                Require(nameof(stats.RushYards), stats.RushYards);
                Require(nameof(stats.RushTouchdowns), stats.RushTouchdowns);
                Require(nameof(stats.Fumbles), stats.Fumbles);
                break;
            default:
                // K/DST structural — detailed fields arrive later.
                break;
        }

        if (missing.Count == 0)
        {
            return (StatsCompleteness.Complete, missing);
        }

        var known = stats.PassYards is not null
                    || stats.RushYards is not null
                    || stats.ReceivingYards is not null
                    || stats.Receptions is not null
                    || stats.PassAttempts is not null;
        return (known ? StatsCompleteness.Partial : StatsCompleteness.Sparse, missing);
    }
}
