using System.Text.RegularExpressions;

namespace Playbook.Application.Users;

/// <summary>Resolves the on-disk data root and the per-user file locations beneath it.</summary>
public static class UserDataPaths
{
    public const string LocalUserId = "local";

    private static readonly Regex SafeId = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

    /// <summary>
    /// PLAYBOOK_DATA_DIR (a mounted persistent volume in production) or ./data beside the binaries.
    /// </summary>
    public static string DataRoot()
    {
        var configured = Environment.GetEnvironmentVariable("PLAYBOOK_DATA_DIR");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : configured;
    }

    /// <summary>
    /// File for <paramref name="fileName"/> belonging to the user. The local/no-user case keeps the
    /// original un-namespaced location so existing single-user data keeps working.
    /// </summary>
    public static string UserFile(string root, IUserContext? user, string fileName)
    {
        var id = user?.UserId;
        if (string.IsNullOrWhiteSpace(id) || id == LocalUserId)
        {
            Directory.CreateDirectory(root);
            return Path.Combine(root, fileName);
        }

        if (!SafeId.IsMatch(id))
        {
            throw new InvalidOperationException("Unsafe user id.");
        }

        var dir = Path.Combine(root, "users", id);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }

    /// <summary>One lock object per absolute path so scoped store instances serialize file access.</summary>
    public static object LockFor(string path) => Locks.GetOrAdd(path, _ => new object());

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Locks =
        new(StringComparer.Ordinal);
}
