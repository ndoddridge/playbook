using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Playbook.Application.Access;
using Playbook.Application.Users;

namespace Playbook.Web.Features.Access;

/// <summary>
/// The signed-in customer for this circuit. Read from the authentication state the framework
/// establishes (from the sign-in cookie) before any component renders; cached for the circuit.
/// </summary>
public sealed class HttpUserContext : IUserContext
{
    public const string AnonymousUserId = "anonymous";

    private readonly AuthenticationStateProvider _auth;
    private readonly bool _open;
    private string? _userId;

    public HttpUserContext(AuthenticationStateProvider auth, IOptions<AccessOptions> options)
    {
        _auth = auth;
        _open = options.Value.Mode == AccessMode.Open;
    }

    public string UserId
    {
        get
        {
            if (_open)
            {
                return UserDataPaths.LocalUserId;
            }

            if (_userId is not null)
            {
                return _userId;
            }

            var task = _auth.GetAuthenticationStateAsync();
            if (!task.IsCompletedSuccessfully)
            {
                return AnonymousUserId;
            }

            var id = task.Result.User.FindFirst(AccessClaims.UserId)?.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                return AnonymousUserId;
            }

            return _userId = id;
        }
    }
}

public static class AccessClaims
{
    public const string UserId = "pb:uid";
    public const string LicenseKey = "pb:lk";
    public const string InstanceId = "pb:inst";
    public const string ValidatedAt = "pb:validated";
}
