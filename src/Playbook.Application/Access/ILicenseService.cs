namespace Playbook.Application.Access;

public enum LicenseFailure
{
    None,
    /// <summary>Key unknown, wrong product/store, disabled, expired or refunded.</summary>
    Invalid,
    /// <summary>The key is real but already active on the maximum number of devices.</summary>
    ActivationLimit,
    /// <summary>Lemon Squeezy could not be reached — says nothing about the key itself.</summary>
    Unavailable
}

public sealed record LicenseResult(bool Success, LicenseFailure Failure, string? Message, string? InstanceId)
{
    public static LicenseResult Ok(string? instanceId) => new(true, LicenseFailure.None, null, instanceId);

    public static LicenseResult Fail(LicenseFailure failure, string message) => new(false, failure, message, null);
}

/// <summary>Checks purchased license keys against the payment provider.</summary>
public interface ILicenseService
{
    /// <summary>Registers this browser/device against the key and returns its instance id.</summary>
    Task<LicenseResult> ActivateAsync(string licenseKey, string instanceName, CancellationToken cancellationToken = default);

    /// <summary>Re-checks that a key (and the instance it was activated with) is still valid.</summary>
    Task<LicenseResult> ValidateAsync(string licenseKey, string? instanceId, CancellationToken cancellationToken = default);
}

/// <summary>Keys known to be refunded/disabled, learned from webhooks; checked on every request.</summary>
public interface ILicenseRevocationStore
{
    bool IsRevoked(string userId);

    void Revoke(string userId);

    void Restore(string userId);
}
