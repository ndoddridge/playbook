using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Playbook.Infrastructure.Access;

namespace Playbook.Tests.Access;

public class LemonSqueezyWebhookTests : IDisposable
{
    private const string Secret = "whsec_test";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pb-revocations-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string Sign(string body, string secret = Secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private static string Payload(string eventName, string key, string status) =>
        "{\"meta\":{\"event_name\":\"" + eventName + "\"},\"data\":{\"attributes\":{\"key\":\"" + key + "\",\"status\":\"" + status + "\"}}}";

    private (LemonSqueezyWebhook Hook, LicenseRevocationStore Store) Create()
    {
        var store = new LicenseRevocationStore(NullLogger<LicenseRevocationStore>.Instance, _root);
        return (new LemonSqueezyWebhook(store, NullLogger<LemonSqueezyWebhook>.Instance), store);
    }

    [Fact]
    public void Valid_Signature_Passes_And_Tampering_Fails()
    {
        var body = Encoding.UTF8.GetBytes("{\"a\":1}");

        Assert.True(LemonSqueezyWebhook.VerifySignature(Secret, body, Sign("{\"a\":1}")));
        Assert.True(LemonSqueezyWebhook.VerifySignature(Secret, body, Sign("{\"a\":1}").ToUpperInvariant()));
        Assert.False(LemonSqueezyWebhook.VerifySignature(Secret, body, Sign("{\"a\":2}")));
        Assert.False(LemonSqueezyWebhook.VerifySignature(Secret, body, Sign("{\"a\":1}", "other-secret")));
        Assert.False(LemonSqueezyWebhook.VerifySignature(Secret, body, null));
        Assert.False(LemonSqueezyWebhook.VerifySignature(Secret, body, ""));
        Assert.False(LemonSqueezyWebhook.VerifySignature("", body, Sign("{\"a\":1}", "")));
    }

    [Fact]
    public void Disabled_License_Is_Revoked_And_Reactivation_Restores()
    {
        var (hook, store) = Create();
        var user = LicenseIdentity.UserIdFor("KEY-1");

        var disabled = Payload("license_key_updated", "KEY-1", "disabled");
        Assert.Equal(WebhookOutcome.Revoked, hook.Process(Secret, Encoding.UTF8.GetBytes(disabled), Sign(disabled)));
        Assert.True(store.IsRevoked(user));

        var active = Payload("license_key_updated", "key-1", "active");
        Assert.Equal(WebhookOutcome.Restored, hook.Process(Secret, Encoding.UTF8.GetBytes(active), Sign(active)));
        Assert.False(store.IsRevoked(user));
    }

    [Fact]
    public void Unsigned_Or_Forged_Webhook_Changes_Nothing()
    {
        var (hook, store) = Create();
        var body = Payload("license_key_updated", "KEY-1", "disabled");

        Assert.Equal(WebhookOutcome.InvalidSignature, hook.Process(Secret, Encoding.UTF8.GetBytes(body), "deadbeef"));
        Assert.Equal(WebhookOutcome.InvalidSignature, hook.Process(Secret, Encoding.UTF8.GetBytes(body), null));
        Assert.False(store.IsRevoked(LicenseIdentity.UserIdFor("KEY-1")));
    }

    [Fact]
    public void Unrelated_Events_Are_Ignored_And_Garbage_Is_Malformed()
    {
        var (hook, _) = Create();

        var order = Payload("order_created", "KEY-1", "active");
        Assert.Equal(WebhookOutcome.Ignored, hook.Process(Secret, Encoding.UTF8.GetBytes(order), Sign(order)));

        const string garbage = "not json";
        Assert.Equal(WebhookOutcome.Malformed, hook.Process(Secret, Encoding.UTF8.GetBytes(garbage), Sign(garbage)));

        const string noKey = """{"meta":{"event_name":"license_key_updated"},"data":{"attributes":{"status":"disabled"}}}""";
        Assert.Equal(WebhookOutcome.Malformed, hook.Process(Secret, Encoding.UTF8.GetBytes(noKey), Sign(noKey)));
    }

    [Fact]
    public void Revocations_Survive_A_Restart()
    {
        var (_, first) = Create();
        first.Revoke("abc");

        var second = new LicenseRevocationStore(NullLogger<LicenseRevocationStore>.Instance, _root);

        Assert.True(second.IsRevoked("abc"));
        Assert.False(second.IsRevoked("other"));
    }
}
