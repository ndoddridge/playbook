namespace Playbook.Core.Research;

/// <summary>
/// A player's accumulated shared evidence, most-recent first. Empty (never fabricated) for a
/// player with no graded research-memory history yet.
/// </summary>
public sealed class PlayerEvidenceSummary
{
    public required Guid PlayerId { get; init; }

    public required IReadOnlyList<PlayerEvidenceItem> Items { get; init; }

    /// <summary>The single highest-weight item's summary, if any — a one-line "what stands out."</summary>
    public required string? Headline { get; init; }

    public bool HasEvidence => Items.Count > 0;
}
