namespace Playbook.Core.Predictions;

/// <summary>
/// Structured signal used by Quick Picks so intelligence weights can be tuned later
/// without changing the UI contract.
/// </summary>
public sealed class PredictionSignalContribution
{
    public required string SignalId { get; init; }

    public required string Label { get; init; }

    /// <summary>False when the signal was unavailable (reduces confidence; never fabricated).</summary>
    public required bool Available { get; init; }

    /// <summary>Configured weight for this signal family (from QuickPicks:Scoring).</summary>
    public decimal Weight { get; init; }

    /// <summary>Multiplier applied to quality (≥0).</summary>
    public decimal QualityMultiplier { get; init; } = 1m;

    public int ConfidenceDelta { get; init; }

    /// <summary>Additive bias on signed edge in market units (before abs).</summary>
    public decimal EdgeBias { get; init; }

    public string? Detail { get; init; }

    /// <summary>When true, UI/reasoning must label this as unconfirmed — never as fact.</summary>
    public bool IsUnconfirmed { get; init; }
}
