using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Access;

namespace Playbook.Infrastructure.Access;

/// <summary>
/// Lemon Squeezy License API client (no API key needed — the license key is the credential).
/// Every response is checked against our own store and product ids, so a valid key from any other
/// Lemon Squeezy merchant can never unlock Playbook.
/// </summary>
public sealed class LemonSqueezyLicenseService : ILicenseService
{
    public const string HttpClientName = "lemonsqueezy-licenses";

    private readonly IHttpClientFactory _http;
    private readonly AccessOptions _options;
    private readonly ILogger<LemonSqueezyLicenseService> _logger;

    public LemonSqueezyLicenseService(
        IHttpClientFactory http,
        IOptions<AccessOptions> options,
        ILogger<LemonSqueezyLicenseService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public Task<LicenseResult> ActivateAsync(string licenseKey, string instanceName, CancellationToken cancellationToken = default) =>
        PostAsync(
            "v1/licenses/activate",
            new Dictionary<string, string>
            {
                ["license_key"] = licenseKey.Trim(),
                ["instance_name"] = string.IsNullOrWhiteSpace(instanceName) ? "Playbook web" : instanceName.Trim()
            },
            successProperty: "activated",
            cancellationToken);

    public Task<LicenseResult> ValidateAsync(string licenseKey, string? instanceId, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["license_key"] = licenseKey.Trim() };
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            form["instance_id"] = instanceId;
        }

        return PostAsync("v1/licenses/validate", form, successProperty: "valid", cancellationToken);
    }

    private async Task<LicenseResult> PostAsync(
        string path,
        Dictionary<string, string> form,
        string successProperty,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form["license_key"]) || form["license_key"].Length > 200)
        {
            return LicenseResult.Fail(LicenseFailure.Invalid, "Enter the license key from your purchase email.");
        }

        try
        {
            var client = _http.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new FormUrlEncodedContent(form)
            };
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogWarning("Lemon Squeezy {Path} returned {Status}.", path, (int)response.StatusCode);
                return LicenseResult.Fail(LicenseFailure.Unavailable, "The license server is unavailable. Please try again in a minute.");
            }

            using var doc = JsonDocument.Parse(body);
            return Interpret(doc.RootElement, successProperty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Lemon Squeezy license check failed to complete.");
            return LicenseResult.Fail(LicenseFailure.Unavailable, "The license server is unavailable. Please try again in a minute.");
        }
    }

    internal LicenseResult Interpret(JsonElement root, string successProperty)
    {
        var ok = root.TryGetProperty(successProperty, out var flag) && flag.ValueKind == JsonValueKind.True;
        var error = root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
            ? err.GetString()
            : null;

        if (!ok)
        {
            var limit = error?.Contains("limit", StringComparison.OrdinalIgnoreCase) == true;
            return limit
                ? LicenseResult.Fail(
                    LicenseFailure.ActivationLimit,
                    "This license key is already in use on the maximum number of devices.")
                : LicenseResult.Fail(LicenseFailure.Invalid, "That license key isn't valid. Check it and try again.");
        }

        // Defence in depth: the key must belong to *our* store and product.
        if (!root.TryGetProperty("meta", out var meta) ||
            !TryGetLong(meta, "store_id", out var storeId) ||
            storeId != _options.StoreId ||
            (_options.ProductId > 0 && (!TryGetLong(meta, "product_id", out var productId) || productId != _options.ProductId)))
        {
            _logger.LogWarning("Rejected a license that belongs to a different Lemon Squeezy store/product.");
            return LicenseResult.Fail(LicenseFailure.Invalid, "That license key isn't valid for Playbook.");
        }

        if (root.TryGetProperty("license_key", out var lk) &&
            lk.TryGetProperty("status", out var status) &&
            status.ValueKind == JsonValueKind.String &&
            status.GetString() is "disabled" or "expired")
        {
            return LicenseResult.Fail(LicenseFailure.Invalid, "This license is no longer active.");
        }

        string? instanceId = null;
        if (root.TryGetProperty("instance", out var instance) &&
            instance.ValueKind == JsonValueKind.Object &&
            instance.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.String)
        {
            instanceId = id.GetString();
        }

        return LicenseResult.Ok(instanceId);
    }

    private static bool TryGetLong(JsonElement element, string name, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var p))
        {
            return false;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(p.GetString(), out value),
            _ => false
        };
    }
}
