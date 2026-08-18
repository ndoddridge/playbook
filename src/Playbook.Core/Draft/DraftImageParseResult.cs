namespace Playbook.Core.Draft;

/// <summary>
/// One pick as extracted from a draft screenshot, before any player/owner identity
/// resolution. Raw text only — resolving these strings to Playbook identities is the
/// ingestion orchestrator's job, not the vision adapter's.
/// </summary>
public sealed record DraftImageParsedPick(
    int? PickNumber,
    int? Round,
    string? OwnerText,
    string? PlayerText,
    string? PositionText,
    bool IsAmbiguous,
    string? AmbiguityReason);

/// <summary>
/// Result of parsing one draft screenshot. Never contains a resolved player/owner
/// identity or a fabricated pick — uncertain reads are flagged via
/// <see cref="DraftImageParsedPick.IsAmbiguous"/> or, when the image couldn't be
/// read as a draft at all, <see cref="Unparseable"/>.
/// </summary>
public sealed record DraftImageParseResult(
    IReadOnlyList<DraftImageParsedPick> Picks,
    IReadOnlyList<string> Warnings,
    bool Unparseable);
