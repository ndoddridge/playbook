using Playbook.Core.Leagues;

namespace Playbook.Core.Draft;

/// <summary>
/// League/draft settings a screenshot can't carry on its own, supplied by the import form.
/// <see cref="IsCompleteDraft"/> is required rather than inferred — the vision model cannot
/// reliably tell a finished draft from an in-progress mock from pixels alone, and guessing
/// would fabricate draft state.
/// </summary>
public sealed record DraftImportContext(
    string LeagueId,
    string Season,
    string LeagueName,
    LeagueType LeagueType,
    string DraftType,
    int TeamCount,
    int RoundCount,
    IReadOnlyDictionary<string, double> ScoringSettings,
    IReadOnlyList<string> RosterSettings,
    IReadOnlyList<string> OwnerNames,
    bool IsCompleteDraft,
    string? MyOwnerName = null);
