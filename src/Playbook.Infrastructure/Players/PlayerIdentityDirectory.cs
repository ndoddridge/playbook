using System.Text.RegularExpressions;
using Playbook.Application.Players;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Players;

public sealed class PlayerIdentityDirectory : IPlayerIdentityDirectory
{
    private static readonly Regex NonNameChars = new(@"[^a-z0-9\s]", RegexOptions.Compiled);

    private readonly object _gate = new();
    private Dictionary<Guid, PlaybookPlayerIdentity> _byPlaybook = new();
    private Dictionary<string, PlaybookPlayerIdentity> _bySleeper = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PlaybookPlayerIdentity> _byGsis = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PlaybookPlayerIdentity> _byEspn = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<PlaybookPlayerIdentity>> _byName = new(StringComparer.OrdinalIgnoreCase);

    public int Count
    {
        get { lock (_gate) { return _byPlaybook.Count; } }
    }

    public int IdentitiesWithGsisId
    {
        get { lock (_gate) { return _byGsis.Count; } }
    }

    public int IdentitiesWithEspnId
    {
        get { lock (_gate) { return _byEspn.Count; } }
    }

    public void ReplaceAll(IEnumerable<PlaybookPlayerIdentity> identities)
    {
        var byPlaybook = new Dictionary<Guid, PlaybookPlayerIdentity>();
        var bySleeper = new Dictionary<string, PlaybookPlayerIdentity>(StringComparer.OrdinalIgnoreCase);
        var byGsis = new Dictionary<string, PlaybookPlayerIdentity>(StringComparer.OrdinalIgnoreCase);
        var byEspn = new Dictionary<string, PlaybookPlayerIdentity>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, List<PlaybookPlayerIdentity>>(StringComparer.OrdinalIgnoreCase);

        foreach (var identity in identities)
        {
            Index(identity, byPlaybook, bySleeper, byGsis, byEspn, byName);
        }

        lock (_gate)
        {
            _byPlaybook = byPlaybook;
            _bySleeper = bySleeper;
            _byGsis = byGsis;
            _byEspn = byEspn;
            _byName = byName;
        }
    }

    public void Upsert(PlaybookPlayerIdentity identity)
    {
        lock (_gate)
        {
            Index(identity, _byPlaybook, _bySleeper, _byGsis, _byEspn, _byName);
        }
    }

    public PlaybookPlayerIdentity? GetByPlaybookId(Guid playbookId)
    {
        lock (_gate)
        {
            return _byPlaybook.GetValueOrDefault(playbookId);
        }
    }

    public PlaybookPlayerIdentity? GetBySleeperId(string sleeperId)
    {
        if (string.IsNullOrWhiteSpace(sleeperId))
        {
            return null;
        }

        lock (_gate)
        {
            return _bySleeper.GetValueOrDefault(sleeperId.Trim());
        }
    }

    public PlaybookPlayerIdentity? GetByGsisId(string gsisId)
    {
        if (string.IsNullOrWhiteSpace(gsisId))
        {
            return null;
        }

        lock (_gate)
        {
            return _byGsis.GetValueOrDefault(gsisId.Trim());
        }
    }

    public PlaybookPlayerIdentity? GetByEspnId(string espnId)
    {
        if (string.IsNullOrWhiteSpace(espnId))
        {
            return null;
        }

        lock (_gate)
        {
            return _byEspn.GetValueOrDefault(espnId.Trim());
        }
    }

    public PlaybookPlayerIdentity? ResolveByNameTeam(string fullName, string? team)
    {
        var key = NormalizeName(fullName);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        lock (_gate)
        {
            if (!_byName.TryGetValue(key, out var candidates) || candidates.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(team))
            {
                // When a team is supplied, require an exact team match — do not guess.
                return candidates.FirstOrDefault(c =>
                    string.Equals(c.Team, team, StringComparison.OrdinalIgnoreCase));
            }

            return candidates.Count == 1 ? candidates[0] : null;
        }
    }

    private static void Index(
        PlaybookPlayerIdentity identity,
        Dictionary<Guid, PlaybookPlayerIdentity> byPlaybook,
        Dictionary<string, PlaybookPlayerIdentity> bySleeper,
        Dictionary<string, PlaybookPlayerIdentity> byGsis,
        Dictionary<string, PlaybookPlayerIdentity> byEspn,
        Dictionary<string, List<PlaybookPlayerIdentity>> byName)
    {
        byPlaybook[identity.PlaybookId] = identity;

        if (!string.IsNullOrWhiteSpace(identity.SleeperId))
        {
            bySleeper[identity.SleeperId.Trim()] = identity;
        }

        if (!string.IsNullOrWhiteSpace(identity.GsisId))
        {
            byGsis[identity.GsisId.Trim()] = identity;
        }

        if (!string.IsNullOrWhiteSpace(identity.EspnId))
        {
            byEspn[identity.EspnId.Trim()] = identity;
        }

        var nameKey = NormalizeName(identity.FullName);
        if (string.IsNullOrWhiteSpace(nameKey))
        {
            return;
        }

        if (!byName.TryGetValue(nameKey, out var list))
        {
            list = [];
            byName[nameKey] = list;
        }

        list.RemoveAll(x => x.PlaybookId == identity.PlaybookId);
        list.Add(identity);
    }

    private static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var cleaned = NonNameChars.Replace(name.ToLowerInvariant(), " ");
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
