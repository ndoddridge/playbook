namespace Playbook.Web;

/// <summary>
/// Shared application identity for footer chrome and diagnostics.
/// </summary>
public static class AppInfo
{
    public const string Version = "1.0.0";

    public const string DisplayName = "Playbook";

    public static string FooterLabel => $"{DisplayName} v{Version}";
}
