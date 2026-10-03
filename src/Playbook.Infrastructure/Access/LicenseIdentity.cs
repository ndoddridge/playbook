using System.Security.Cryptography;
using System.Text;

namespace Playbook.Infrastructure.Access;

/// <summary>Derives the stable, filesystem-safe user id from a license key (the key never hits disk).</summary>
public static class LicenseIdentity
{
    public static string UserIdFor(string licenseKey)
    {
        var normalized = (licenseKey ?? string.Empty).Trim().ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("playbook-license:" + normalized));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }
}
