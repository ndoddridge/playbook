using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Playbook.Application.Access;
using Playbook.Infrastructure.Access;

namespace Playbook.Tests.Access;

public class LemonSqueezyLicenseServiceTests
{
    private const long Store = 111;
    private const long Product = 222;

    private static string Ok(string flag, long store = Store, long product = Product, string status = "active", string instance = "inst-9") =>
        "{\"" + flag + "\": true, \"error\": null, " +
        "\"license_key\": {\"status\": \"" + status + "\", \"key\": \"K\"}, " +
        "\"instance\": {\"id\": \"" + instance + "\", \"name\": \"n\"}, " +
        "\"meta\": {\"store_id\": " + store + ", \"product_id\": " + product + "}}";

    [Fact]
    public async Task Activate_Succeeds_And_Returns_Instance_Id()
    {
        var (svc, handler) = Create(Ok("activated"));

        var result = await svc.ActivateAsync("  ABC-123  ", "Playbook web");

        Assert.True(result.Success);
        Assert.Equal("inst-9", result.InstanceId);
        Assert.Equal("/v1/licenses/activate", handler.LastPath);
        Assert.Contains("license_key=ABC-123", handler.LastBody);
        Assert.Contains("instance_name=Playbook+web", handler.LastBody);
    }

    [Fact]
    public async Task Key_From_Another_Store_Is_Rejected()
    {
        var (svc, _) = Create(Ok("activated", store: 999));

        var result = await svc.ActivateAsync("K", "x");

        Assert.False(result.Success);
        Assert.Equal(LicenseFailure.Invalid, result.Failure);
    }

    [Fact]
    public async Task Key_For_Another_Product_Is_Rejected()
    {
        var (svc, _) = Create(Ok("valid", product: 7));

        var result = await svc.ValidateAsync("K", "inst-9");

        Assert.Equal(LicenseFailure.Invalid, result.Failure);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("expired")]
    public async Task Disabled_Or_Expired_Key_Is_Rejected(string status)
    {
        var (svc, _) = Create(Ok("valid", status: status));

        var result = await svc.ValidateAsync("K", null);

        Assert.False(result.Success);
        Assert.Equal(LicenseFailure.Invalid, result.Failure);
    }

    [Fact]
    public async Task Unknown_Key_Is_Invalid()
    {
        var (svc, _) = Create("""{"activated": false, "error": "license_key not found.", "meta": null}""");

        Assert.Equal(LicenseFailure.Invalid, (await svc.ActivateAsync("nope", "x")).Failure);
    }

    [Fact]
    public async Task Activation_Limit_Is_Reported_Distinctly()
    {
        var (svc, _) = Create("""{"activated": false, "error": "This license key has reached the activation limit.", "meta": null}""");

        Assert.Equal(LicenseFailure.ActivationLimit, (await svc.ActivateAsync("K", "x")).Failure);
    }

    [Fact]
    public async Task Server_Error_Means_Unavailable_Not_Invalid()
    {
        var (svc, _) = Create("oops", HttpStatusCode.BadGateway);

        // A provider outage must never be mistaken for "this customer's key is bad".
        Assert.Equal(LicenseFailure.Unavailable, (await svc.ValidateAsync("K", "i")).Failure);
    }

    [Fact]
    public async Task Network_Failure_Means_Unavailable()
    {
        var (svc, _) = Create(throwOnSend: true);

        Assert.Equal(LicenseFailure.Unavailable, (await svc.ValidateAsync("K", "i")).Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_Key_Is_Rejected_Without_Calling_Out(string key)
    {
        var (svc, handler) = Create(Ok("valid"));

        var result = await svc.ActivateAsync(key, "x");

        Assert.Equal(LicenseFailure.Invalid, result.Failure);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Validate_Sends_Instance_Id()
    {
        var (svc, handler) = Create(Ok("valid"));

        await svc.ValidateAsync("K", "inst-9");

        Assert.Equal("/v1/licenses/validate", handler.LastPath);
        Assert.Contains("instance_id=inst-9", handler.LastBody);
    }

    [Fact]
    public void LicenseRequired_Config_Reports_Every_Missing_Setting()
    {
        var problems = new AccessOptions { Mode = AccessMode.LicenseRequired }.Validate();

        Assert.Equal(4, problems.Count);
        Assert.Empty(new AccessOptions { Mode = AccessMode.Open }.Validate());
        Assert.Empty(new AccessOptions
        {
            StoreId = 1, ProductId = 2, WebhookSecret = "s", CheckoutUrl = "https://x.lemonsqueezy.com/checkout/buy/abc"
        }.Validate());
        Assert.Single(new AccessOptions
        {
            StoreId = 1, ProductId = 2, WebhookSecret = "s", CheckoutUrl = "http://insecure.example/buy"
        }.Validate());
    }

    [Fact]
    public void Admin_Ids_Parse_And_Match_Case_Insensitively()
    {
        var options = new AccessOptions { AdminUserIds = " abc123 , DEF456 " };

        Assert.True(options.IsAdmin("ABC123"));
        Assert.True(options.IsAdmin("def456"));
        Assert.False(options.IsAdmin("other"));
        Assert.False(options.IsAdmin(""));
        Assert.False(new AccessOptions().IsAdmin("abc123"));
    }

    [Fact]
    public void User_Id_Is_Stable_Case_Insensitive_And_Filesystem_Safe()
    {
        var a = LicenseIdentity.UserIdFor("abcd-1234");

        Assert.Equal(a, LicenseIdentity.UserIdFor("  ABCD-1234 "));
        Assert.NotEqual(a, LicenseIdentity.UserIdFor("abcd-1235"));
        Assert.Matches("^[a-f0-9]{32}$", a);
        Assert.DoesNotContain("abcd", a);
    }

    private static (LemonSqueezyLicenseService Service, RecordingHandler Handler) Create(
        string body = "{}", HttpStatusCode status = HttpStatusCode.OK, bool throwOnSend = false)
    {
        var handler = new RecordingHandler(body, status, throwOnSend);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.lemonsqueezy.com/") };
        var service = new LemonSqueezyLicenseService(
            new SingleClientFactory(client),
            Options.Create(new AccessOptions { StoreId = Store, ProductId = Product }),
            NullLogger<LemonSqueezyLicenseService>.Instance);
        return (service, handler);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(string body, HttpStatusCode status, bool throwOnSend) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string LastBody { get; private set; } = string.Empty;
        public string LastPath { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastPath = request.RequestUri!.AbsolutePath;
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (throwOnSend)
            {
                throw new HttpRequestException("boom");
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
