namespace Playbook.Web;

/// <summary>
/// Shared application identity for developer-facing chrome (footer, status cards).
/// </summary>
public static class AppInfo
{
    public const string Version = "0.1.0-dev";

    public const string DisplayName = "Playbook";

    public static string FooterLabel => $"{DisplayName} v{Version}";
}
