using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Playbook.Application.Access;
using Playbook.Application.Users;
using Playbook.Infrastructure.Access;

namespace Playbook.Web.Features.Access;

public static class AccessSetup
{
    public const string CookieName = "playbook.session";
    public const string UnlockRateLimit = "unlock";
    private static readonly TimeSpan MaxOfflineGrace = TimeSpan.FromDays(7);

    public static IServiceCollection AddPlaybookAccess(this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)
    {
        var access = configuration.GetSection(AccessOptions.SectionName).Get<AccessOptions>() ?? new AccessOptions();

        var problems = access.Validate();
        if (problems.Count > 0 && !env.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Playbook is configured to require paid access but the payment settings are incomplete:\n - " +
                string.Join("\n - ", problems) +
                "\nSet them as environment variables (Access__StoreId, Access__ProductId, Access__CheckoutUrl, " +
                "Access__WebhookSecret) or set Access__Mode=Open to run without a paywall (never in production).");
        }

        // Sign-in cookies and antiforgery tokens are encrypted with these keys; if they lived in the
        // container's ephemeral disk every deploy would log all customers out.
        services.AddDataProtection()
            .SetApplicationName("playbook")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(UserDataPaths.DataRoot(), "dp-keys")));

        services.AddScoped<IUserContext, HttpUserContext>();
        services.AddCascadingAuthenticationState();

        if (access.Mode == AccessMode.Open)
        {
            // Local development / tests: everyone is the single local user, no sign-in.
            services.AddAuthentication(OpenAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, OpenAuthenticationHandler>(OpenAuthenticationHandler.SchemeName, _ => { });
            services.AddRateLimiter(o => o.AddPolicy(UnlockRateLimit, _ => RateLimitPartition.GetNoLimiter("open")));
            return services;
        }

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromDays(Math.Clamp(access.SessionDays, 1, 365));
                options.SlidingExpiration = true;
                options.LoginPath = "/unlock";
                options.AccessDeniedPath = "/unlock";
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = ValidateLicenseAsync
                };
            });

        // Default-deny: every component under Features/ carries [Authorize] (see Features/_Imports.razor);
        // only the marketing/legal/sign-in pages in Components/Pages are public.
        services.AddAuthorization();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(UnlockRateLimit, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    private sealed class OpenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "PlaybookOpen";

        public OpenAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(AccessClaims.UserId, UserDataPaths.LocalUserId)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private static async Task ValidateLicenseAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var options = services.GetRequiredService<IOptions<AccessOptions>>().Value;
        var principal = context.Principal;
        var userId = principal?.FindFirstValue(AccessClaims.UserId);
        if (principal is null || string.IsNullOrEmpty(userId))
        {
            await RejectAsync(context);
            return;
        }

        if (services.GetRequiredService<ILicenseRevocationStore>().IsRevoked(userId))
        {
            await RejectAsync(context);
            return;
        }

        var validated = long.TryParse(principal.FindFirstValue(AccessClaims.ValidatedAt), NumberStyles.None, CultureInfo.InvariantCulture, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : DateTimeOffset.MinValue;
        var age = DateTimeOffset.UtcNow - validated;
        if (age < TimeSpan.FromHours(Math.Clamp(options.RevalidateHours, 1, 168)))
        {
            return;
        }

        var key = principal.FindFirstValue(AccessClaims.LicenseKey);
        if (string.IsNullOrEmpty(key))
        {
            await RejectAsync(context);
            return;
        }

        var result = await services.GetRequiredService<ILicenseService>()
            .ValidateAsync(key, principal.FindFirstValue(AccessClaims.InstanceId), context.HttpContext.RequestAborted);

        if (result.Success)
        {
            context.ReplacePrincipal(BuildPrincipal(key, principal.FindFirstValue(AccessClaims.InstanceId), DateTimeOffset.UtcNow));
            context.ShouldRenew = true;
        }
        else if (result.Failure != LicenseFailure.Unavailable || age > MaxOfflineGrace)
        {
            // Refunded/disabled/expired — or Lemon Squeezy has been unreachable for over a week.
            // A short outage never locks out a paying customer.
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public static ClaimsPrincipal BuildPrincipal(string licenseKey, string? instanceId, DateTimeOffset validatedAt)
    {
        var claims = new List<Claim>
        {
            new(AccessClaims.UserId, LicenseIdentity.UserIdFor(licenseKey)),
            new(AccessClaims.LicenseKey, licenseKey.Trim()),
            new(AccessClaims.ValidatedAt, validatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        };
        if (!string.IsNullOrEmpty(instanceId))
        {
            claims.Add(new Claim(AccessClaims.InstanceId, instanceId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static void MapAccessEndpoints(this WebApplication app)
    {
        var access = app.Services.GetRequiredService<IOptions<AccessOptions>>().Value;

        app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous();

        app.MapPost("/access/unlock", async (
                HttpContext http,
                IAntiforgery antiforgery,
                ILicenseService licenses,
                ILicenseRevocationStore revocations,
                ILogger<Program> logger) =>
            {
                if (access.Mode == AccessMode.Open)
                {
                    return Results.Redirect("/dashboard");
                }

                // A stale or missing antiforgery token (form open for days, cookies cleared) sends the
                // customer back to a fresh form instead of an error page.
                if (!await antiforgery.IsRequestValidAsync(http))
                {
                    return Results.Redirect("/unlock?error=expired");
                }

                var form = await http.Request.ReadFormAsync(http.RequestAborted);
                var licenseKey = form["licenseKey"].FirstOrDefault();
                var returnUrl = form["returnUrl"].FirstOrDefault();

                var result = await licenses.ActivateAsync(
                    licenseKey ?? string.Empty,
                    $"Playbook web {DateTime.UtcNow:yyyy-MM-dd}",
                    http.RequestAborted);

                if (!result.Success)
                {
                    logger.LogInformation("Unlock rejected: {Failure}", result.Failure);
                    return Results.Redirect($"/unlock?error={result.Failure.ToString().ToLowerInvariant()}");
                }

                var key = licenseKey!.Trim();
                revocations.Restore(LicenseIdentity.UserIdFor(key));
                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    BuildPrincipal(key, result.InstanceId, DateTimeOffset.UtcNow),
                    new AuthenticationProperties { IsPersistent = true });

                return Results.LocalRedirect(IsSafeLocalPath(returnUrl) ? returnUrl! : "/dashboard");
            })
            .AllowAnonymous()
            .RequireRateLimiting(UnlockRateLimit);

        app.MapPost("/access/logout", async (HttpContext http) =>
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Redirect("/");
            })
            .AllowAnonymous();

        app.MapPost("/webhooks/lemonsqueezy", async (HttpContext http, LemonSqueezyWebhook webhook) =>
            {
                if (http.Request.ContentLength is > 1_000_000)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                using var buffer = new MemoryStream();
                await http.Request.Body.CopyToAsync(buffer, http.RequestAborted);
                var outcome = webhook.Process(access.WebhookSecret, buffer.ToArray(), http.Request.Headers["X-Signature"].FirstOrDefault());
                return outcome switch
                {
                    WebhookOutcome.InvalidSignature => Results.Unauthorized(),
                    WebhookOutcome.Malformed => Results.BadRequest(),
                    _ => Results.Ok()
                };
            })
            .AllowAnonymous()
            .DisableAntiforgery();
    }

    private static bool IsSafeLocalPath(string? path) =>
        !string.IsNullOrEmpty(path) &&
        path[0] == '/' &&
        !path.StartsWith("//", StringComparison.Ordinal) &&
        !path.StartsWith("/\\", StringComparison.Ordinal);
}
