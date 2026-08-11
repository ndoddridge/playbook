using Microsoft.AspNetCore.HttpOverrides;
using Playbook.Application;
using Playbook.Infrastructure;
using Playbook.Web.Components;
using Playbook.Web.Features.QuickPicks.Interfaces;
using Playbook.Web.Features.QuickPicks.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddInfrastructure(builder.Configuration)
    .AddApplication();

builder.Services.AddSingleton<IQuickPicksBoard, QuickPicksBoard>();

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
}

// Skip HTTPS redirection in Development so phone/tunnel HTTP access works.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
