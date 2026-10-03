namespace Playbook.Application.Users;

/// <summary>
/// Identifies the customer a scoped service is working for. Per-user state (connected leagues,
/// selected roster, personal draft learning) is keyed by <see cref="UserId"/>; shared football
/// data (players, stats, news, Quick Picks) never is.
/// </summary>
public interface IUserContext
{
    /// <summary>Stable, filesystem-safe identifier. <see cref="LocalUserId"/> when access control is off.</summary>
    string UserId { get; }

    static string LocalUserId => UserDataPaths.LocalUserId;
}

/// <summary>Fixed user used by tests, the research CLI and open (no paywall) local development.</summary>
public sealed class LocalUserContext : IUserContext
{
    public string UserId => UserDataPaths.LocalUserId;
}
