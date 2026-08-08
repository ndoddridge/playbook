using System.Security.Cryptography;
using System.Text;

namespace Playbook.Infrastructure.Players;

/// <summary>
/// Shared Sleeper player id ↔ Playbook Guid mapping used by catalog and stats providers.
/// </summary>
public static class SleeperPlayerIds
{
    public static Guid ToPlaybookId(string sleeperId)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"playbook:sleeper:nfl:{sleeperId}"));
        return new Guid(bytes);
    }
}
