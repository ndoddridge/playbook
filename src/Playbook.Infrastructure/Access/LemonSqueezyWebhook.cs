using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Playbook.Application.Access;

namespace Playbook.Infrastructure.Access;

public enum WebhookOutcome
{
    Ignored,
    Revoked,
    Restored,
    InvalidSignature,
    Malformed
}

/// <summary>
/// Verifies and applies Lemon Squeezy webhooks. Playbook only needs one thing from them: when a
/// license key is disabled (refund/chargeback) it stops working immediately instead of at the next
/// periodic re-check. Subscribe to <c>license_key_updated</c> (and optionally <c>order_refunded</c>).
/// </summary>
public sealed class LemonSqueezyWebhook
{
    private readonly ILicenseRevocationStore _revocations;
    private readonly ILogger<LemonSqueezyWebhook> _logger;

    public LemonSqueezyWebhook(ILicenseRevocationStore revocations, ILogger<LemonSqueezyWebhook> logger)
    {
        _revocations = revocations;
        _logger = logger;
    }

    /// <summary>Constant-time check of the hex HMAC-SHA256 <c>X-Signature</c> header.</summary>
    public static bool VerifySignature(string secret, ReadOnlySpan<byte> rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), rawBody));
        var provided = signatureHeader.Trim();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(provided.ToLowerInvariant()));
    }

    public WebhookOutcome Process(string secret, byte[] rawBody, string? signatureHeader)
    {
        if (!VerifySignature(secret, rawBody, signatureHeader))
        {
            return WebhookOutcome.InvalidSignature;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            var eventName = root.GetProperty("meta").GetProperty("event_name").GetString();
            if (eventName is not ("license_key_updated" or "license_key_created"))
            {
                return WebhookOutcome.Ignored;
            }

            var attributes = root.GetProperty("data").GetProperty("attributes");
            var key = attributes.GetProperty("key").GetString();
            var status = attributes.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(key))
            {
                return WebhookOutcome.Malformed;
            }

            var userId = LicenseIdentity.UserIdFor(key);
            if (status is "disabled" or "expired")
            {
                _revocations.Revoke(userId);
                _logger.LogInformation("License {UserId} revoked via webhook ({Status}).", userId, status);
                return WebhookOutcome.Revoked;
            }

            if (status is "active" or "inactive")
            {
                _revocations.Restore(userId);
                return WebhookOutcome.Restored;
            }

            return WebhookOutcome.Ignored;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Malformed Lemon Squeezy webhook payload.");
            return WebhookOutcome.Malformed;
        }
    }
}
