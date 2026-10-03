using Microsoft.AspNetCore.HttpOverrides;
using Playbook.Application;
using Playbook.Application.Access;
using Playbook.Infrastructure;
using Playbook.Web.Components;
using Playbook.Web.Features.Access;
using Playbook.Web.Features.QuickPicks.Interfaces;
using Playbook.Web.Features.QuickPicks.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // Exception details only ever reach the browser in Development.
        options.DetailedErrors = builder.Environment.IsDevelopment();
    });

builder.Services
    .AddInfrastructure(builder.Configuration)
    .AddApplication();

builder.Services.AddScoped<IQuickPicksBoard, QuickPicksBoard>();

// Paid access (Lemon Squeezy licenses), sign-in cookie, per-user context. Registered after
// AddInfrastructure so its HttpUserContext replaces the local single-user default.
builder.Services.AddPlaybookAccess(builder.Configuration, builder.Environment);

var app = builder.Build();

// Behind Fly.io's edge proxy, TLS terminates before the request reaches this
// container; trust its X-Forwarded-* headers so UseHttpsRedirection/UseHsts
// see the original scheme instead of redirect-looping on internal HTTP.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownNetworks = { },
    KnownProxies = { }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-Frame-Options"] = "DENY";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

// Friendly 404 page for browsers. GET only: re-executing a failed POST (bad antiforgery token, bad
// webhook signature) would replay its body into the not-found page.
app.UseWhen(
    context => HttpMethods.IsGet(context.Request.Method),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found"));

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAccessEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Exposed for integration tests and ILogger<Program>.
public partial class Program;
