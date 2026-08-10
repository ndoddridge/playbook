using Playbook.Core.Players;

namespace Playbook.Application.Players;

/// <summary>
/// In-memory player identity crosswalk. Populated by data providers; consumed by injury/stats joins.
/// </summary>
public interface IPlayerIdentityDirectory
{
    int Count { get; }

    int IdentitiesWithGsisId { get; }

    int IdentitiesWithEspnId { get; }

    void ReplaceAll(IEnumerable<PlaybookPlayerIdentity> identities);

    void Upsert(PlaybookPlayerIdentity identity);

    PlaybookPlayerIdentity? GetByPlaybookId(Guid playbookId);

    PlaybookPlayerIdentity? GetBySleeperId(string sleeperId);

    PlaybookPlayerIdentity? GetByGsisId(string gsisId);

    PlaybookPlayerIdentity? GetByEspnId(string espnId);

    PlaybookPlayerIdentity? ResolveByNameTeam(string fullName, string? team);
}
