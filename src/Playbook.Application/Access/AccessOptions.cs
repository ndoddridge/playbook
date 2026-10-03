namespace Playbook.Application.Access;

public enum AccessMode
{
    /// <summary>No paywall — everyone is the single local user. Local development and tests only.</summary>
    Open,

    /// <summary>A valid Lemon Squeezy license key is required for the product (production).</summary>
    LicenseRequired
}

/// <summary>Paywall configuration (<c>Access</c> section; override with <c>Access__*</c> env vars).</summary>
public sealed class AccessOptions
{
    public const string SectionName = "Access";

    public AccessMode Mode { get; set; } = AccessMode.LicenseRequired;

    /// <summary>Lemon Squeezy hosted-checkout URL the "Buy" buttons point to.</summary>
    public string CheckoutUrl { get; set; } = string.Empty;

    /// <summary>Display price on the marketing page, e.g. "$4.99". Purely cosmetic.</summary>
    public string PriceLabel { get; set; } = "$4.99";

    /// <summary>Support contact shown in the footer, legal pages and error states.</summary>
    public string SupportEmail { get; set; } = string.Empty;

    /// <summary>Lemon Squeezy store id; licenses from any other store are rejected.</summary>
    public long StoreId { get; set; }

    /// <summary>Lemon Squeezy product id; licenses for any other product are rejected.</summary>
    public long ProductId { get; set; }

    /// <summary>Secret used to verify webhook signatures (Settings → Webhooks → signing secret).</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Comma-separated account ids (see the Account page) allowed to see the operations monitor.</summary>
    public string AdminUserIds { get; set; } = string.Empty;

    public bool IsAdmin(string userId) =>
        !string.IsNullOrWhiteSpace(userId) &&
        AdminUserIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(userId, StringComparer.OrdinalIgnoreCase);

    public string ApiBaseUrl { get; set; } = "https://api.lemonsqueezy.com/";

    /// <summary>How long a sign-in lasts without the user returning.</summary>
    public int SessionDays { get; set; } = 90;

    /// <summary>How often a signed-in license is re-checked with Lemon Squeezy (refund/disable detection).</summary>
    public int RevalidateHours { get; set; } = 12;

    /// <summary>Returns a list of configuration problems that make LicenseRequired mode unusable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (Mode != AccessMode.LicenseRequired)
        {
            return problems;
        }

        if (StoreId <= 0)
        {
            problems.Add("Access:StoreId (Lemon Squeezy store id) is not set.");
        }

        if (ProductId <= 0)
        {
            problems.Add("Access:ProductId (Lemon Squeezy product id) is not set.");
        }

        if (!Uri.TryCreate(CheckoutUrl, UriKind.Absolute, out var checkout) || checkout.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add("Access:CheckoutUrl must be the https Lemon Squeezy checkout link.");
        }

        if (string.IsNullOrWhiteSpace(WebhookSecret))
        {
            problems.Add("Access:WebhookSecret (webhook signing secret) is not set.");
        }

        return problems;
    }
}
