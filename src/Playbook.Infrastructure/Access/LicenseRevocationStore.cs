using System.Text.Json;
using Microsoft.Extensions.Logging;
using Playbook.Application.Access;
using Playbook.Application.Users;

namespace Playbook.Infrastructure.Access;

/// <summary>File-backed (persistent volume) set of revoked user ids, mirrored in memory.</summary>
public sealed class LicenseRevocationStore : ILicenseRevocationStore
{
    private readonly string _path;
    private readonly object _gate;
    private readonly HashSet<string> _revoked;
    private readonly ILogger<LicenseRevocationStore> _logger;

    public LicenseRevocationStore(ILogger<LicenseRevocationStore> logger, string? rootOverride = null)
    {
        _logger = logger;
        var root = rootOverride ?? UserDataPaths.DataRoot();
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "revoked-licenses.json");
        _gate = UserDataPaths.LockFor(_path);
        _revoked = Load();
    }

    public bool IsRevoked(string userId)
    {
        lock (_gate)
        {
            return _revoked.Contains(userId);
        }
    }

    public void Revoke(string userId)
    {
        lock (_gate)
        {
            if (_revoked.Add(userId))
            {
                Save();
            }
        }
    }

    public void Restore(string userId)
    {
        lock (_gate)
        {
            if (_revoked.Remove(userId))
            {
                Save();
            }
        }
    }

    private HashSet<string> Load()
    {
        try
        {
            return File.Exists(_path)
                ? new HashSet<string>(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? [], StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read {Path}; starting with no revocations.", _path);
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_revoked.Order(StringComparer.Ordinal)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not persist revoked licenses to {Path}.", _path);
        }
    }
}
