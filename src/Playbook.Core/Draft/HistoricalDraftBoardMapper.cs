using Playbook.Core.Historical;

namespace Playbook.Core.Draft;

/// <summary>
/// Maps a resolved-but-unsaved historical draft (an in-progress mock ingested from a screenshot)
/// into the same <see cref="DraftBoard"/> shape the live polling path produces, so
/// <see cref="PersonalDraftTendencyPolicy"/> can run over both without duplicating tendency logic.
/// </summary>
public static class HistoricalDraftBoardMapper
{
    /// <summary>
    /// <paramref name="myOwnerName"/> must match one of <see cref="HistoricalLeagueDraft.Owners"/>
    /// exactly (case-insensitive) — when it doesn't (or is omitted), the board's
    /// <see cref="DraftBoard.UserRosterId"/> is null, so no tendencies are computed rather than
    /// guessed at.
    /// </summary>
    public static DraftBoard ToSyntheticDraftBoard(this HistoricalLeagueDraft draft, string? myOwnerName = null)
    {
        var ownerRosterIds = draft.Owners
            .Select((o, i) => (o.DisplayName, RosterId: i + 1))
            .ToDictionary(x => x.DisplayName, x => x.RosterId, StringComparer.OrdinalIgnoreCase);

        var myRosterId = myOwnerName is not null && ownerRosterIds.TryGetValue(myOwnerName, out var rid)
            ? rid
            : (int?)null;

        var picks = draft.Picks
            .OrderBy(p => p.PickNumber)
            .Select(p => new DraftPickRecord
            {
                PickNumber = p.PickNumber,
                Round = p.Round,
                DraftSlot = p.DraftSlot,
                RosterId = ownerRosterIds.GetValueOrDefault(p.OwnerName),
                PlayerId = p.PlaybookPlayerId,
                PlayerName = p.PlayerName,
                PositionLabel = p.Position,
                IsKeeper = p.IsKeeper
            })
            .ToList();

        return new DraftBoard
        {
            DraftId = draft.HistoricalDraftId,
            LeagueId = Guid.Empty,
            Season = draft.Season,
            Status = DraftStatus.Drafting,
            Type = draft.DraftType,
            TotalRounds = draft.RoundCount,
            TeamCount = draft.TeamCount,
            Picks = picks,
            NextPickNumber = picks.Count + 1,
            UserRosterId = myRosterId,
            RetrievedAt = DateTimeOffset.UtcNow
        };
    }
}
